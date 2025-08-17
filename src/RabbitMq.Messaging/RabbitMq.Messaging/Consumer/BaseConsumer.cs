using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Threading.Channels;

namespace RabbitMq.Messaging.Consumer
{
    public abstract class BaseConsumer<TNotification> : BackgroundService where TNotification : class
    {
        // Internal channels for decoupling and inter-task communication.
        // Using System.Threading.Channels is a modern and efficient way to manage concurrent data pipelines.

        /// <summary>
        /// A dedicated channel to enqueue the DeliveryTags of messages that have been processed (either successfully or with a handled failure)
        /// and are ready to be acknowledged (ACKed) in RabbitMQ.
        /// </summary>
        private readonly Channel<ulong> _ackChannel;

        /// <summary>
        /// The main channel that acts as a buffer. The RabbitMQ consumer writes received messages here,
        /// and the parallel workers read from it for processing.
        /// </summary>
        private readonly Channel<MessageSnapshot> _messageChannel;

        /// <summary>
        /// The shared, long-lived, and thread-safe connection to the RabbitMQ server.
        /// </summary>
        private readonly IConnection _connection;

        // Names of queues and exchanges that will be defined during the topology declaration.
        private string _retryHandlerExchange = null!;
        private string _retryQueue = null!;
        private string _dlxExchangeName = null!;
        private string _dlqQueueName = null!;
        private string _directToQueueKey = null!;
        private string _directToRetryKey = null!;
        private readonly string _exchangeRequeue = null!;

        // --- Protected Members ---
        protected readonly ILogger _logger;
        protected readonly string _exchangeName;
        protected readonly string _queueName;

        /// <summary>
        /// Defines the number of worker tasks that will process messages in parallel.
        /// The default value is 5. This can be overridden by the derived class.
        /// </summary>
        protected int _parallelWorkerCount = 5;

        /// <summary>
        /// Defines the prefetch count for the consumer channel. A value of 0 means unlimited, which is discouraged.
        /// A safe default is calculated if this value is not overridden.
        /// </summary>
        protected ushort _prefetchCount = 0;

        // Variables to retry attempts and ttl milliseconds, it's the same for all consumers
        /// <summary>
        /// Defines the maximum number of retry attempts before sending a message to the DLQ.
        /// </summary>
        private readonly int _maxRetryAttempts;

        /// <summary>
        /// Defines the time in milliseconds that a message will stay in the retry queue before being reprocessed.
        /// </summary>
        private readonly int _retryTtlMilliseconds;

        /// <summary>
        /// Constructor for the BaseConsumer class.
        /// </summary>
        /// <param name="connection">The singleton connection to RabbitMQ.</param>
        /// <param name="logger">The logger for recording information and errors.</param>
        /// <param name="exchangeName">The name of the main exchange to consume from.</param>
        /// <param name="queueName">The name of the main queue to be created and consumed.</param>
        public BaseConsumer(
            IConfiguration configuration,
            IConnection connection,
            ILogger logger,
            string exchangeName,
            string queueName)
        {
            _connection = connection;
            _logger = logger;
            _exchangeName = exchangeName;
            _queueName = queueName;

            _messageChannel = Channel.CreateUnbounded<MessageSnapshot>();
            _ackChannel = Channel.CreateUnbounded<ulong>(new UnboundedChannelOptions { SingleReader = true });

            // Retry attempts and ttl milliseconds
            _maxRetryAttempts = configuration.GetValue<int?>("RabbitMq:MaxRetryAttempts") ?? 3;
            _retryTtlMilliseconds = configuration.GetValue<int?>("RabbitMq:RetryTTlMilliseconds") ?? 30000;
            _exchangeRequeue = configuration.GetValue<string?>("RabbitMq:ExchangeRequeue") ?? "exchange-requeue";
        }

        /// <summary>
        /// This is the main method that derived classes must implement.
        /// It contains the specific business logic for processing a message.
        /// </summary>
        /// <param name="messageBody">A byte array containing the message payload. This is a safe copy.</param>
        /// <param name="properties">The properties of the message, including headers.</param>
        /// <param name="cancellationToken">A token to observe for cancellation signals.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        protected abstract Task HandleMessageAsync(byte[] messageBody, IReadOnlyBasicProperties properties, CancellationToken cancellationToken);

        /// <summary>
        /// The entry point of the BackgroundService. It orchestrates the setup and lifecycle of the consumer.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting consumer for queue [{QueueName}]...", _queueName);

            // 1. Use a temporary channel to declare the entire topology of exchanges and queues.
            // The declaration is idempotent, so it's safe to run on startup.
            using var setupChannel = await _connection.CreateChannelAsync();
            await DeclareTopologyAsync(setupChannel);

            // 2. Create the channel that will actually receive messages from RabbitMQ.
            var consumerChannel = await _connection.CreateChannelAsync();
            await consumerChannel.BasicQosAsync(0, _prefetchCount, false);

            // 3. Start the background support tasks.
            var ackerTask = StartAckerTask(consumerChannel, stoppingToken);
            var workerTasks = Enumerable.Range(0, _parallelWorkerCount)
                .Select(_ => StartWorkerAsync(stoppingToken))
                .ToList();

            // 4. Configure and start the consumer that reads from RabbitMQ and feeds the _messageChannel.
            var consumer = new AsyncEventingBasicConsumer(consumerChannel);
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                var snapshot = new MessageSnapshot(
                    ea.Body.ToArray(),      // This creates a new byte array, making our copy safe.
                    ea.BasicProperties,
                    ea.DeliveryTag
                );
                // Just forwards the message to the internal buffer, no processing here.
                await _messageChannel.Writer.WriteAsync(snapshot, stoppingToken);
            };
            await consumerChannel.BasicConsumeAsync(queue: _queueName, autoAck: false, consumer: consumer);

            // 5. Wait for all tasks to complete for a graceful shutdown.
            await Task.WhenAll(workerTasks.Concat(new[] { ackerTask }));
            await consumerChannel.CloseAsync();
        }

        /// <summary>
        /// Starts a long-running task dedicated to acknowledging messages (ACK).
        /// This pattern solves the "channel mismatch" problem by ensuring all ACKs
        /// are executed on the same channel where the messages were received.
        /// </summary>
        /// <param name="channel">The original consumer channel, on which the ACKs are valid.</param>
        /// <param name="stoppingToken">A token for shutting down the task.</param>
        private async Task StartAckerTask(IChannel channel, CancellationToken stoppingToken)
        {
            _logger.LogInformation("Centralized Acker task started.");
            await foreach (var deliveryTag in _ackChannel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    // Acknowledge the message on the correct RabbitMQ channel.
                    await channel.BasicAckAsync(deliveryTag, false);
                    _logger.LogTrace("Message with DeliveryTag {DeliveryTag} acknowledged (ACK).", deliveryTag);
                }
                catch (Exception ex)
                {
                    // A failure here is critical, as it might lead to reprocessing a message that was already handled.
                    _logger.LogCritical(ex, "Critical error in Acker Task while acknowledging DeliveryTag {DeliveryTag}. The message might be reprocessed.", deliveryTag);
                }
            }
        }

        /// <summary>
        /// Defines the logic of a worker. A worker continuously reads from the _messageChannel,
        /// processes the message, and delegates the acknowledgment to the Acker Task.
        /// </summary>
        private async Task StartWorkerAsync(CancellationToken stoppingToken)
        {
            // Each worker gets its own channel to publish to retry/DLQ queues.
            // This avoids concurrency issues from using a shared channel.
            using var processingChannel = await _connection.CreateChannelAsync();

            await foreach (var snapshot in _messageChannel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    var retryCount = GetRetryCount(snapshot.Properties);
                    if (retryCount >= _maxRetryAttempts)
                    {
                        _logger.LogWarning("[WORKER] Max retries ({MaxRetryAttempts}) reached. Sending to DLQ.", _maxRetryAttempts);
                        await PublishToDlqAsync(processingChannel, snapshot);
                    }
                    else
                    {
                        try
                        {
                            // Call the abstract method with the actual business logic.
                            await HandleMessageAsync(snapshot.Body, snapshot.Properties, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            // If the business logic fails, publish to the retry queue.
                            _logger.LogError(ex, "[WORKER] Error processing message. Sending to retry ({RetryCount})...", retryCount + 1);
                            await PublishToRetryAsync(processingChannel, snapshot, ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // This catch block captures errors in the worker's own logic (e.g., failing to publish to the DLQ).
                    // In these cases, the best strategy is not to acknowledge the message, allowing RabbitMQ to redeliver it.
                    _logger.LogCritical(ex, "[WORKER] Unrecoverable critical error in worker. The message will NOT be acknowledged and will be redelivered.");
                    continue; // Skips sending the DeliveryTag to the ACK channel.
                }

                // After handling (success, retry, or DLQ), send the DeliveryTag to the centralized Acker Task.
                // The responsibility of sending the ACK is transferred.
                await _ackChannel.Writer.WriteAsync(snapshot.DeliveryTag, stoppingToken);
            }
        }

        /// <summary>
        /// Declares all necessary topology in RabbitMQ (exchanges, queues, and bindings).
        /// </summary>
        private async Task DeclareTopologyAsync(IChannel channel)
        {
            // Define names for all components based on the main exchange and queue names for isolation.
            _retryHandlerExchange = $"{_exchangeName}-retry-handler";
            _retryQueue = $"{_queueName}-retry";
            _dlxExchangeName = $"{_exchangeName}-{_queueName}-dlx";
            _dlqQueueName = $"{_queueName}-dlq";

            // Define unique routing keys for this specific consumer's queue.
            _directToQueueKey = $"key-direct-to-{_queueName}";
            _directToRetryKey = $"key-direct-to-retry-{_queueName}";

            // 1. Declare the main exchange (Fanout) for broadcasting new messages.
            await channel.ExchangeDeclareAsync(exchange: _exchangeName, type: ExchangeType.Fanout, durable: true);

            // 2. Declare the retry handler exchange (Direct) for targeted redelivery and requeue.
            await channel.ExchangeDeclareAsync(exchange: _retryHandlerExchange, type: ExchangeType.Direct, durable: true);
            await channel.ExchangeDeclareAsync(exchange: _exchangeRequeue, type: ExchangeType.Direct, durable: true);

            // 3. Declare the main queue and its bindings.
            await channel.QueueDeclareAsync(queue: _queueName, durable: true, exclusive: false, autoDelete: false);
            // Bind 1: To the Fanout exchange to receive all new broadcast messages.
            await channel.QueueBindAsync(queue: _queueName, exchange: _exchangeName, routingKey: "");
            // Bind 2: To the Direct exchange to receive messages being requeued after a retry.
            await channel.QueueBindAsync(queue: _queueName, exchange: _retryHandlerExchange, routingKey: _directToQueueKey);
            // Bind 3: To the Direct exchange requeue
            await channel.QueueBindAsync(queue: _queueName, exchange: _exchangeRequeue, routingKey: _queueName);

            // 4. Declare the retry queue and its arguments.
            var retryQueueArgs = new Dictionary<string, object>
            {
                // Defines how long the message will be "dead" in the retry queue (Time-To-Live).
                { "x-message-ttl", _retryTtlMilliseconds },
                // After TTL expires, the message goes to our Direct retry handler exchange...
                { "x-dead-letter-exchange", _retryHandlerExchange },
                // ...using the routing key that points it back to the main queue.
                { "x-dead-letter-routing-key", _directToQueueKey }
            };

            await channel.QueueDeclareAsync(queue: _retryQueue, durable: true, exclusive: false, autoDelete: false, arguments: retryQueueArgs);
            // Bind the retry queue to the Direct exchange to receive failed messages from the worker.
            await channel.QueueBindAsync(queue: _retryQueue, exchange: _retryHandlerExchange, routingKey: _directToRetryKey);

            // 5. Declare the final Dead-Letter Queue (DLQ) for analysis of failed messages.
            await channel.ExchangeDeclareAsync(exchange: _dlxExchangeName, type: ExchangeType.Direct, durable: true);
            await channel.QueueDeclareAsync(queue: _dlqQueueName, durable: true, exclusive: false, autoDelete: false);
            await channel.QueueBindAsync(queue: _dlqQueueName, exchange: _dlxExchangeName, routingKey: "");
        }

        /// <summary>
        /// Publishes a message to the retry exchange.
        /// </summary>
        private ValueTask PublishToRetryAsync(IChannel channel, MessageSnapshot snapshot, Exception ex)
        {
            var newProps = CreateClonedProperties(snapshot.Properties);
            AddExceptionInfoToHeaders(newProps, ex);

            // Publishes to the Direct exchange using the key that leads to the retry queue.
            return channel.BasicPublishAsync(
                exchange: _retryHandlerExchange,
                routingKey: _directToRetryKey,
                mandatory: true,
                basicProperties: newProps,
                body: snapshot.Body);
        }

        /// <summary>
        /// Publishes a message to the Dead-Letter Exchange (DLX).
        /// </summary>
        private ValueTask PublishToDlqAsync(IChannel channel, MessageSnapshot snapshot)
        {
            // The properties already contain the exception info from the last failed retry.
            var newProps = CreateClonedProperties(snapshot.Properties);

            return channel.BasicPublishAsync(
                exchange: _dlxExchangeName,
                routingKey: "", // Routing to a specific DLQ is handled by its unique exchange name.
                mandatory: true,
                basicProperties: newProps,
                body: snapshot.Body);
        }

        /// <summary>
        /// Creates a new, mutable BasicProperties object by cloning the values from the original readonly properties.
        /// This allows preserving information like CorrelationId, ContentType, and Headers for the new message.
        /// </summary>
        private BasicProperties CreateClonedProperties(IReadOnlyBasicProperties originalProps)
        {
            // Instantiate the new 'BasicProperties' struct, which implements IAmqpHeader.
            var newProps = new BasicProperties();

            // Copy common properties. Add others here if they are relevant to your system.
            newProps.CorrelationId = originalProps.CorrelationId;
            newProps.ContentType = originalProps.ContentType;
            newProps.ContentEncoding = originalProps.ContentEncoding;
            newProps.DeliveryMode = originalProps.DeliveryMode;
            newProps.Expiration = originalProps.Expiration;
            newProps.MessageId = originalProps.MessageId;
            newProps.ReplyTo = originalProps.ReplyTo;
            newProps.Timestamp = originalProps.Timestamp;
            newProps.Type = originalProps.Type;
            newProps.UserId = originalProps.UserId;

            // Ensure the new message will be persisted.
            newProps.Persistent = true;

            // It's crucial to create a NEW dictionary for the headers to avoid modifying the original collection.
            newProps.Headers = originalProps.Headers != null
                ? new Dictionary<string, object>(originalProps.Headers)
                : new Dictionary<string, object>();

            return newProps;
        }

        /// <summary>
        /// Adds detailed information from an exception to the message's properties headers.
        /// Uses the 'x-' prefix, which is a common convention for custom headers.
        /// </summary>
        /// <param name="props">The message properties object to modify.</param>
        /// <param name="ex">The exception that occurred.</param>
        private void AddExceptionInfoToHeaders(IBasicProperties props, Exception ex)
        {
            // The Headers dictionary is guaranteed to exist by CreateClonedProperties.
            props.Headers["x-exception-message"] = ex.Message;
            props.Headers["x-exception-stacktrace"] = ex.ToString(); // ToString() includes the stacktrace and inner exceptions.
            props.Headers["x-exception-timestamp"] = DateTime.UtcNow.ToString("o"); // ISO 8601 format
        }

        /// <summary>
        /// Extracts the retry count from the 'x-death' header of the message.
        /// </summary>
        /// <remarks>
        /// This is a simple implementation. A more robust version would also check the queue name
        /// within the 'x-death' header to avoid miscounting retries from other queues.
        /// </remarks>
        private static int GetRetryCount(IReadOnlyBasicProperties props)
        {
            if (props.Headers != null && props.Headers.TryGetValue("x-death", out var deathHeader))
            {
                var xDeathList = deathHeader as List<object>;
                if (xDeathList?.FirstOrDefault() is Dictionary<string, object> deathInfo &&
                    deathInfo.TryGetValue("count", out var countObj))
                {
                    return Convert.ToInt32(countObj);
                }
            }
            return 0;
        }
    }
}
