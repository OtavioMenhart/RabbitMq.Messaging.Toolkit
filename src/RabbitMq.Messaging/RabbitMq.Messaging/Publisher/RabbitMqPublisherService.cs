using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace RabbitMq.Messaging.Publisher
{
    public class RabbitMqPublisherService : IRabbitMqPublisherService
    {
        private readonly IConnection _connection;
        private readonly ILogger<RabbitMqPublisherService> _logger;

        public RabbitMqPublisherService(IConnection connection, ILogger<RabbitMqPublisherService> logger)
        {
            _connection = connection;
            _logger = logger;
        }

        /// <summary>
        /// Publishes a message to a specified exchange. This method is thread-safe.
        /// </summary>
        /// <typeparam name="T">The type of the message object.</typeparam>
        /// <param name="message">The message object to be serialized and published.</param>
        /// <param name="exchangeName">The name of the exchange to publish to.</param>
        /// <param name="headers">Optional dictionary of headers to add to the message.</param>
        /// <param name="serializerOptions">Json serializer option for the message.</param>
        /// <param name="exchangeType">The type of the exchange (e.g., Fanout, Direct, Topic). Defaults to Fanout.</param>
        /// <param name="routingKey">The routing key for the message. Defaults to empty (for Fanout exchanges).</param>
        public async Task PublishMessage<T>(
            T message,
            string exchangeName,
            Dictionary<string, string>? headers = null,
            JsonSerializerOptions? serializerOptions = null,
            string exchangeType = ExchangeType.Fanout,
            string routingKey = "")
        {
            using var channel = await _connection.CreateChannelAsync().ConfigureAwait(false);

            try
            {
                await channel.ExchangeDeclareAsync(
                    exchange: exchangeName,
                    type: exchangeType,
                    durable: true,
                    autoDelete: false,
                    arguments: null
                ).ConfigureAwait(false);

                var jsonMessage = JsonSerializer.Serialize(message, serializerOptions);
                var body = Encoding.UTF8.GetBytes(jsonMessage);

                // Use the modern BasicProperties struct.
                var props = new BasicProperties
                {
                    // Ensure messages are persisted to disk, surviving a broker restart.
                    Persistent = true,
                    // Set content type for better interoperability.
                    ContentType = "application/json"
                };

                // Correctly add headers if they are provided.
                if (headers != null && headers.Any())
                {
                    // Headers are a dictionary of string -> object.
                    // The client library handles the encoding of string values.
                    props.Headers = headers.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
                }

                _logger.LogTrace("Publishing message to exchange '{ExchangeName}' with routing key '{RoutingKey}'", exchangeName, routingKey);

                // Publish the message using the specified exchange and routing key.
                await channel.BasicPublishAsync(
                    exchange: exchangeName,
                    routingKey: routingKey,
                    mandatory: true,
                    basicProperties: props,
                    body: body
                ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish message to exchange '{ExchangeName}'", exchangeName);
                throw;
            }
        }
    }
}
