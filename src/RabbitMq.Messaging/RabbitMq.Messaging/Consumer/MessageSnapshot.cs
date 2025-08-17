using RabbitMQ.Client;

namespace RabbitMq.Messaging.Consumer
{
    /// <summary>
    /// Represents a safe, immutable snapshot of a received message.
    /// This is crucial to prevent issues with buffer reuse by the RabbitMQ client library.
    /// </summary>
    public record MessageSnapshot(byte[] Body, IReadOnlyBasicProperties Properties, ulong DeliveryTag);
}
