using Microsoft.Extensions.Logging;
using Moq;
using RabbitMq.Messaging.Publisher;
using RabbitMQ.Client;
using System.Text.Json;

namespace RabbitMq.Messaging.Tests.UnitTests;

public class RabbitMqPublisherServiceTests
{
    [Fact]
    public async Task PublishMessage_PublishesMessageWithCorrectParameters()
    {        
        // Arrange
        var mockConnection = new Mock<IConnection>();
        var mockChannel = new Mock<IChannel>();
        var mockLogger = new Mock<ILogger<RabbitMqPublisherService>>();

        mockConnection
            .Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockChannel.Object);

        mockChannel
            .Setup(c => c.ExchangeDeclareAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockChannel
            .Setup(c => c.BasicPublishAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<BasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()
             ))
            .Returns(new ValueTask());


        var service = new RabbitMqPublisherService(mockConnection.Object, mockLogger.Object);

        var testMessage = new { Text = "Hello" };
        var exchangeName = "test-exchange";
        var headers = new Dictionary<string, string> { { "key", "value" } };
        var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var exchangeType = "direct";
        var routingKey = "test-key";

        // Act
        await service.PublishMessage(
            testMessage,
            exchangeName,
            headers,
            serializerOptions,
            exchangeType,
            routingKey);

        // Assert
        mockChannel.Verify(c => c.ExchangeDeclareAsync(
            It.Is<string>(s => s == exchangeName),
            It.Is<string>(s => s == exchangeType),
            It.Is<bool>(b => b == true),
            It.Is<bool>(b => b == false),
            It.Is<IDictionary<string, object?>?>(d => d == null),
            It.Is<bool>(b => b == false),
            It.Is<bool>(b => b == false),
            It.IsAny<CancellationToken>()), Times.Once);

        mockChannel.Verify(c => c.BasicPublishAsync(
            It.Is<string>(s => s == exchangeName),
            It.Is<string>(s => s == routingKey),
            It.Is<bool>(b => b == true),
            It.Is<BasicProperties>(p =>
                p.Persistent == true &&
                p.ContentType == "application/json" &&
                p.Headers != null &&
                p.Headers.ContainsKey("key") &&
                ($"{p.Headers["key"]}" == "value")),
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishMessage_LogsErrorOnException()
    {
        // Arrange
        var mockConnection = new Mock<IConnection>();
        var mockChannel = new Mock<IChannel>();
        var mockLogger = new Mock<ILogger<RabbitMqPublisherService>>();

        mockConnection
            .Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockChannel.Object);

        mockChannel
            .Setup(c => c.ExchangeDeclareAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Exchange error"));

        var service = new RabbitMqPublisherService(mockConnection.Object, mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PublishMessage(new { }, "ex"));

        mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => ($"{v}").Contains("Failed to publish message")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
