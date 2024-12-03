using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Webhook.Utilities.Contracts;
using Webhook.Utilities.Services;

namespace Webhook.Utilities.Utils
{
    public static class DIExtensions
    {
        //public static IServiceCollection AddWebhookListenerServices(this IServiceCollection services, IConfiguration config)
        //{

        //    services.AddSingleton<EnvironmentVariables>(provider =>
        //    {
        //        var config = provider.GetService<IConfiguration>();

        //        // Bind configuration/environment variables
        //        return new()
        //        {
        //            ServiceBusTopic = config.GetValue<string>("ServiceBus:Topic"),
        //            ServiceBusTopicSubscription = config.GetValue<string>("ServiceBus:TopicSubscription")
        //        };
        //    });
            
        //    // Register Application services
        //    services.AddScoped<IPublisherService, PublisherService>();

        //    // Register ServiceBus instances
        //    //_ = bool.TryParse(Environment.GetEnvironmentVariable("ServiceBus:UseManagedIdentity"), out bool useManagedIdentity);
        //    //services.AddServiceBusClientAndSender(useManagedIdentity,
        //    //                                        Environment.GetEnvironmentVariable("ServiceBus:Topic"),
        //    //                                        Environment.GetEnvironmentVariable("ServiceBus:Name"),
        //    //                                        Environment.GetEnvironmentVariable("ServiceBus:ConnectionString"));
        //    services.AddServiceBusClientAndSender(config.GetValue<bool>("ServiceBus:UseManagedIdentity"),
        //                                            config.GetValue<string>("ServiceBus:Topic"),
        //                                            config.GetValue<string>("ServiceBus:Name"),
        //                                            config.GetValue<string>("ServiceBus:ConnectionString"));
        //    return services;
        //}

        public static IServiceCollection AddServiceBusClientAndSender(this IServiceCollection services,
                                                                  bool useManagedIdentity,
                                                                  string topicName,
                                                                  string? serviceBusName,
                                                                  string? serviceBusConnectionString
                                                                  )
        {
            services.AddAzureClients(builder =>
            {
                if (useManagedIdentity)
                {
                    // Adding using managed identity
                    builder.AddServiceBusClientWithNamespace($"{serviceBusName}.servicebus.windows.net");
                }
                else
                {
                    // Adding using connection string
                    builder.AddServiceBusClient(serviceBusConnectionString);
                }

                builder.AddClient<ServiceBusSender, ServiceBusClientOptions>((_, _, provider) =>
                    provider
                        .GetRequiredService<ServiceBusClient>()
                        .CreateSender(topicName)
                )
                .WithName(topicName);
            });

            return services;
        }
    }
}
