using AutoFixture;
using AutoFixture.AutoNSubstitute;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RabbitMq.Messaging.Publisher;
using RabbitMQ.Client;
using System.Text.Json;

namespace RabbitMq.Messaging.Tests.UnitTests;

public class RabbitMqPublisherServiceTests
{
    private readonly IFixture _fixture;

    public RabbitMqPublisherServiceTests()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization { ConfigureMembers = true });
    }

    [Fact]
    public async Task PublishMessage_PublishesMessageWithCorrectParameters()
    {
        // Arrange
        var mockConnection = _fixture.Freeze<IConnection>();
        var mockChannel = _fixture.Freeze<IChannel>();
        var mockLogger = _fixture.Freeze<ILogger<RabbitMqPublisherService>>();

        mockConnection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(mockChannel);

        mockChannel
            .ExchangeDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        mockChannel
            .BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(new ValueTask());

        var service = new RabbitMqPublisherService(mockConnection, mockLogger);

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
        await mockChannel.Received(1).ExchangeDeclareAsync(
            Arg.Is<string>(s => s == exchangeName),
            Arg.Is<string>(s => s == exchangeType),
            Arg.Is<bool>(b => b == true),
            Arg.Is<bool>(b => b == false),
            Arg.Is<IDictionary<string, object?>?>(d => d == null),
            Arg.Is<bool>(b => b == false),
            Arg.Is<bool>(b => b == false),
            Arg.Any<CancellationToken>());

        await mockChannel.Received(1).BasicPublishAsync(
            Arg.Is<string>(s => s == exchangeName),
            Arg.Is<string>(s => s == routingKey),
            Arg.Is<bool>(b => b == true),
            Arg.Is<BasicProperties>(p =>
                p.Persistent == true &&
                p.ContentType == "application/json" &&
                p.Headers != null &&
                p.Headers.ContainsKey("key") &&
                ($"{p.Headers["key"]}" == "value")),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishMessage_LogsErrorOnException()
    {
        // Arrange
        var mockConnection = _fixture.Freeze<IConnection>();
        var mockChannel = _fixture.Freeze<IChannel>();
        var mockLogger = _fixture.Freeze<ILogger<RabbitMqPublisherService>>();

        mockConnection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(mockChannel);

        mockChannel
            .ExchangeDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Exchange error"));

        var service = new RabbitMqPublisherService(mockConnection, mockLogger);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PublishMessage(new { }, "ex"));

        mockLogger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString().Contains("Failed to publish message")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
