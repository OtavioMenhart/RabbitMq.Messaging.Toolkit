using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMq.Messaging.Consumer;
using RabbitMQ.Client;

namespace RabbitMq.Messaging.Tests.UnitTests
{
    public class TestConsumer : BaseConsumer<object>
    {
        // Ação a ser executada quando HandleMessageAsync for chamado.
        // Isso nos permite simular sucesso ou falha.
        public Func<Task> HandleMessageAction { get; set; } = () => Task.CompletedTask;

        // Armazena a última mensagem recebida para que possamos inspecioná-la nos testes.
        public byte[]? LastReceivedMessageBody { get; private set; }
        public IReadOnlyBasicProperties? LastReceivedProperties { get; private set; }

        public TestConsumer(
            IConfiguration configuration,
            IConnection connection,
            ILogger<TestConsumer> logger,
            IEnumerable<ExchangeBinding> mainExchangeBindings,
            string queueName)
            : base(configuration, connection, logger, mainExchangeBindings, queueName)
        {
            // Reduzimos o número de workers para 1 para simplificar os testes e evitar
            // condições de corrida em ambientes de teste.
            _parallelWorkerCount = 1;
        }

        protected override async Task HandleMessageAsync(byte[] messageBody, IReadOnlyBasicProperties properties, CancellationToken cancellationToken)
        {
            LastReceivedMessageBody = messageBody;
            LastReceivedProperties = properties;

            // Executa a ação definida no teste (pode ser um sucesso ou lançar uma exceção).
            await HandleMessageAction();
        }
    }
}
