# RabbitMq.Messaging.Toolkit

**RabbitMq.Messaging.Toolkit** is a **.NET 9** library that provides a simple and robust abstraction for working with **RabbitMQ**.  
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
[Route("api/[controller]")]
[ApiController]
public class UsersController : ControllerBase
{
    private readonly IRabbitMqPublisherService _publisher;

    public UsersController(IRabbitMqPublisherService publisher)
    {
        _publisher = publisher;
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] NewUserDto newUser)
    {
        // Fanout
        await _publisher.PublishMessage(newUser, "new-user-exchange");

        // Direct
        await _publisher.PublishMessage(newUser, "new-user-exchange-direct", exchangeType: ExchangeType.Direct, routingKey: "user.created");

        // Topic
        await _publisher.PublishMessage(newUser, "new-user-exchange-topic", exchangeType: ExchangeType.Topic, routingKey: "user.whatever");

        return Ok($"User '{newUser.Name}' created successfully.");
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
public class UserCreatedConsumer : BaseConsumer<NewUserDto>
{
    public UserCreatedConsumer(
        IConfiguration config,
        IConnection conn,
        ILogger<UserCreatedConsumer> logger
    ) : base(config, conn, logger, new[] {
        new ExchangeBinding("new-user-exchange", ExchangeType.Fanout, ""),
        new ExchangeBinding("new-user-exchange-direct", ExchangeType.Direct, "user.created"),
        new ExchangeBinding("new-user-exchange-topic", ExchangeType.Topic, "user.*")
    }, "user-created-queue") { }

    protected override async Task HandleMessageAsync(
        byte[] messageBody,
        IReadOnlyBasicProperties properties,
        CancellationToken cancellationToken)
    {
        var user = JsonSerializer.Deserialize<NewUserDto>(messageBody);
        // Lógica de processamento do usuário
        await Task.CompletedTask;
    }
}
```

**Constructor parameters:**
- `IConfiguration` → for Retry/DLQ settings  
- `IConnection` → RabbitMQ connection  
- `ILogger` → injected logger  
- `IEnumerable<ExchangeBinding>` → exchange bindings  
- `queueName` → queue name  

### 🔑 Registering the Consumer

To activate the consumer, you must register it as a **HostedService**:

```csharp
services.AddHostedService<UserCreatedConsumer>();
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
