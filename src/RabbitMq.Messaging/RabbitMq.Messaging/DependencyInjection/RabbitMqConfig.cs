using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMq.Messaging.Configurations;
using RabbitMq.Messaging.Publisher;
using RabbitMQ.Client;

namespace RabbitMq.Messaging.DependencyInjection
{
    public static class RabbitMqConfig
    {
        /// <summary>
        /// Add the RabbitMq connection
        /// </summary> 
        /// <param name="services">The IServiceCollection</param> 
        /// <param name="configuration">The IConfiguration, should have RabbitMq section on appsettings</param> 
        /// <returns>RabbitMq connection created</returns> 
        /// <exception cref="ArgumentException">If no hosts be informed</exception> 
        public static IServiceCollection AddRabbitMqInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            var rabbitMqSection = configuration.GetSection(RabbitMqOptions.SectionName);
            services.Configure<RabbitMqOptions>(rabbitMqSection.Bind);
            services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
                if (options.Hosts == null || options.Hosts.Count == 0)
                {
                    throw new ArgumentException("At least one RabbitMQ host should be configured.", nameof(options.Hosts));
                }

                var factory = new ConnectionFactory
                {
                    UserName = options.UserName,
                    Password = options.Password,
                    VirtualHost = options.VirtualHost,
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(options.NetworkRecoveryInterval)
                };

                var endpoints = options.Hosts
                                       .Select(host => new AmqpTcpEndpoint(host, options.Port))
                                       .ToList();

                return factory.CreateConnectionAsync(endpoints).GetAwaiter().GetResult();
            });
            return services;
        }

        /// <summary>
        /// Publisher RabbitMq dependency injection
        /// </summary> 
        public static IServiceCollection AddRabbitMqPublisher(this IServiceCollection services)
        {
            services.AddSingleton<IRabbitMqPublisherService, RabbitMqPublisherService>();
            return services;
        }
    }
}
