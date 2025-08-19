# RabbitMq.Messaging

**RabbitMq.Messaging** is a **.NET 9** library that provides a simple and robust abstraction for working with **RabbitMQ**.  
It offers ready-to-use **publisher** and **consumer services**, making it easy to integrate message-based communication and background processing into your projects.

---

## ✨ Features

- 🚀 **Publisher Service**: Publish messages to RabbitMQ exchanges with custom headers, routing keys, and serialization options.  
- 🛠️ **Consumer Base Class**: Build resilient consumers with support for **parallel processing, retries, and dead-letter queues**.  
- 🔗 **Dependency Injection**: Direct integration with **ASP.NET Core DI**.  
- ⚙️ **Configuration via appsettings**: Centralize RabbitMQ connection and behavior configuration.  

---

## ⚡ Getting Started

### 1️⃣ Install Dependencies

Ensure your project targets **.NET 9** and includes the following packages:

- `RabbitMQ.Client`
- `Microsoft.Extensions.*` (Configuration, DependencyInjection, Logging, Options, Hosting)

---

### 2️⃣ Configure RabbitMQ

Add the following section to **appsettings.json**:

```json
{
  "RabbitMq": {
    "Hosts": [ "localhost" ],
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/",
    "NetworkRecoveryInterval": 10
  }
}
```

**Required fields**:
- `Hosts` (at least 1 host)  
- `Port`  
- `UserName`  
- `Password`  
- `VirtualHost`  

**Optional fields**:
- `NetworkRecoveryInterval` → seconds between recovery attempts  

---

### 3️⃣ Register Services

In your **DI** setup (example: `Startup.cs`):

```csharp
services.AddRabbitMqInfrastructure(Configuration);
services.AddRabbitMqPublisher();
```

---

## 📤 Publisher Usage

Inject `IRabbitMqPublisherService` and publish messages:

```csharp
public class MyService
{
    private readonly IRabbitMqPublisherService _publisher;

    public MyService(IRabbitMqPublisherService publisher)
    {
        _publisher = publisher;
    }

    public async Task SendMessageAsync()
    {
        var message = new { Text = "Hello RabbitMQ!" };
        await _publisher.PublishMessage(
            message,
            exchangeName: "my-exchange",
            headers: new Dictionary<string, string> { { "custom-header", "value" } },
            exchangeType: RabbitMQ.Client.ExchangeType.Fanout,
            routingKey: "" // Usually empty for Fanout
        );
    }
}
```

**Parameters:**
- `message` → object serialized as JSON  
- `exchangeName` → target exchange  
- `headers` → custom headers (optional)  
- `serializerOptions` → JSON serialization options (optional)  
- `exchangeType` → Fanout, Direct, Topic (optional)  
- `routingKey` → routing key (optional)  

---

## 📥 Consumer Usage

Create a consumer by inheriting from `BaseConsumer<TNotification>`:

```csharp
public class MyConsumer : BaseConsumer<MyNotification>
{
    public MyConsumer(
        IConfiguration config,
        IConnection conn,
        ILogger logger
    ) : base(config, conn, logger, "my-exchange", "my-queue") { }

    protected override async Task HandleMessageAsync(
        byte[] messageBody,
        IReadOnlyBasicProperties properties,
        CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<MyNotification>(messageBody);
        // Processing logic...
        await Task.CompletedTask;
    }
}
```

**Constructor parameters:**
- `IConfiguration` → for Retry/DLQ settings  
- `IConnection` → RabbitMQ connection  
- `ILogger` → injected logger  
- `exchangeName` → exchange name  
- `queueName` → queue name  

### 🔑 Registering the Consumer

To activate the consumer, you must register it as a **HostedService**:

```csharp
services.AddHostedService<MyConsumer>();
```

---

**Additional configuration in `appsettings.json`:**
```json
{
  "RabbitMq": {
    "MaxRetryAttempts": 3,
    "RetryTTlMilliseconds": 30000
  }
}
```

---

## 📌 Summary

With **RabbitMq.Messaging** you get:
- Simplified publishers 📨  
- Resilient consumers ⚡  
- Centralized configuration 🔧  
- Ready-to-use ASP.NET Core integration 💡  
