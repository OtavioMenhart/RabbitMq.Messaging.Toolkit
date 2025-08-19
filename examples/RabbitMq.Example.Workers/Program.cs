using RabbitMq.Example.Workers;
using RabbitMq.Messaging.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// Add RabbitMq infrastructure
builder.Services.AddRabbitMqInfrastructure(builder.Configuration);

// Register RabbitMq consumer
builder.Services.AddHostedService<NewUserConsumer>();

var host = builder.Build();
host.Run();
