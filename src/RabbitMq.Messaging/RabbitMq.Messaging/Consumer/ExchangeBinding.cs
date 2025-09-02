namespace RabbitMq.Messaging.Consumer
{
    /// <summary>
    /// Represents a binding between exchanges in a message broker, including the exchange name, type, and routing key.
    /// </summary>
    /// <param name="ExchangeName">The name of the exchange to bind. This value cannot be null or empty.</param>
    /// <param name="ExchangeType">The type of the exchange (e.g., "direct", "fanout", "topic", or "headers"). This value cannot be null or empty.</param>
    /// <param name="RoutingKey">The routing key used for the binding. This value may be null or empty, depending on the exchange type.</param>
    public record ExchangeBinding(string ExchangeName, string ExchangeType, string RoutingKey);
}
