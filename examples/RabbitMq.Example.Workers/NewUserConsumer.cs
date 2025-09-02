using RabbitMq.Example.Domain.Dto;
using RabbitMq.Messaging.Consumer;
using RabbitMQ.Client;
using System.Text.Json;

namespace RabbitMq.Example.Workers
{
    public class NewUserConsumer : BaseConsumer<NewUserDto>
    {
        public NewUserConsumer(
            IConfiguration configuration,
            IConnection connection,
            ILogger<NewUserConsumer> logger)
            : base(
                  configuration,
                  connection,
                  logger,
                  new[]
                  {
                      new ExchangeBinding
                      (
                          ExchangeName: "new-user-exchange",
                          ExchangeType: ExchangeType.Fanout,
                          RoutingKey: ""
                      ),
                      new ExchangeBinding
                      (
                          ExchangeName: "new-user-exchange-direct",
                          ExchangeType: ExchangeType.Direct,
                          RoutingKey: "user.created"
                      ),
                      new ExchangeBinding
                      (
                          ExchangeName: "new-user-exchange-topic",
                          ExchangeType: ExchangeType.Topic,
                          RoutingKey: "user.*"
                      )
                  },
                  "new.user.queue")
        {
            // You can configure additional settings for the consumer here if needed.
            _prefetchCount = 50;
            _parallelWorkerCount = 10;
        }

        protected override async Task HandleMessageAsync(byte[] messageBody, IReadOnlyBasicProperties properties, CancellationToken cancellationToken)
        {
            var user = JsonSerializer.Deserialize<NewUserDto>(messageBody);
            if (user != null)
            {
                _logger.LogInformation($"[NewUserConsumer] Received new user: Name={user.Name}, Age={user.Age}");
                // To simulate an exception for testing purposes, uncomment the line below:
                // throw new Exception("Simulated exception for testing purposes.");
            }
            else
            {
                _logger.LogWarning("[NewUserConsumer] Failed to deserialize NewUserDto.");
            }
            await Task.CompletedTask;
        }
    }
}