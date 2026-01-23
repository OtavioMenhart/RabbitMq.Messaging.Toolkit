using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMq.Messaging.Configurations;
using RabbitMq.Messaging.DependencyInjection;
using RabbitMq.Messaging.Publisher;
using RabbitMQ.Client;

namespace RabbitMq.Messaging.Tests.UnitTests;

public class RabbitMqConfigTests
{
    [Fact]
    public void AddRabbitMqInfrastructure_WithValidConfiguration_ShouldRegisterServicesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var inMemorySettings = new Dictionary<string, string>
        {
            {"RabbitMq:Hosts:0", "localhost"},
            {"RabbitMq:UserName", "guest"},
            {"RabbitMq:Password", "guest"},
            {"RabbitMq:Port", "5672"},
            {"RabbitMq:VirtualHost", "/"}
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Act
        services.AddRabbitMqInfrastructure(configuration);

        // Assert
        // 1. Verifica se o IOptions<RabbitMqOptions> foi configurado
        var optionsDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConfigureOptions<RabbitMqOptions>));
        Assert.NotNull(optionsDescriptor);

        // 2. Verifica se a conexão IConnection foi registrada como Singleton
        var connectionDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConnection));
        Assert.NotNull(connectionDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, connectionDescriptor.Lifetime);

        // 3. (Opcional) Verifica se as opções foram carregadas corretamente
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        Assert.Single(options.Hosts);
        Assert.Equal("localhost", options.Hosts[0]);
        Assert.Equal("guest", options.UserName);
        Assert.Equal(5672, options.Port);
    }

    [Fact]
    public void AddRabbitMqInfrastructure_WhenNoHostsConfigured_ShouldThrowArgumentExceptionOnServiceResolution()
    {
        // Arrange
        var services = new ServiceCollection();
        // Configuração sem a seção "Hosts"
        var inMemorySettings = new Dictionary<string, string>
        {
            {"RabbitMq:UserName", "guest"},
            {"RabbitMq:Password", "guest"}
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Act
        services.AddRabbitMqInfrastructure(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        // A exceção é lançada quando o serviço IConnection é solicitado (resolvido), não no registro.
        var exception = Assert.Throws<ArgumentException>(() => serviceProvider.GetRequiredService<IConnection>());

        Assert.Equal("At least one RabbitMQ host should be configured. (Parameter 'Hosts')", exception.Message);
    }

    [Fact]
    public void AddRabbitMqInfrastructure_WhenHostsArrayIsEmpty_ShouldThrowArgumentExceptionOnServiceResolution()
    {
        // Arrange
        var services = new ServiceCollection();
        // Configuração com uma lista de Hosts vazia.
        // O binder de configuração criará uma lista vazia se a chave existir, mas não tiver valores.
        var inMemorySettings = new Dictionary<string, string>
        {
            {"RabbitMq:Hosts", ""}, // Simula uma seção vazia
            {"RabbitMq:UserName", "guest"}
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Act
        services.AddRabbitMqInfrastructure(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var exception = Assert.Throws<ArgumentException>(() => serviceProvider.GetRequiredService<IConnection>());

        Assert.Equal("At least one RabbitMQ host should be configured. (Parameter 'Hosts')", exception.Message);
    }

    [Fact]
    public void AddRabbitMqPublisher_WhenCalled_ShouldRegisterPublisherServiceAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRabbitMqPublisher();

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRabbitMqPublisherService));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(RabbitMqPublisherService), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}