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

        public static IServiceCollection AddServiceBusClientAndReceiver(this IServiceCollection services,
                                                                  bool useManagedIdentity,
                                                                  string topicName,
                                                                  string subscriptionName,
                                                                  string serviceBusName,
                                                                  string serviceBusConnectionString
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

                ServiceBusReceiverOptions receiverOptions = new()
                {
                    PrefetchCount = 20
                };
                builder.AddClient<ServiceBusReceiver, ServiceBusClientOptions>((_, _, provider) =>
                    provider
                        .GetRequiredService<ServiceBusClient>()
                        .CreateReceiver(topicName, subscriptionName, receiverOptions)
                )
                .WithName(subscriptionName);
            });

            return services;
        }

        public static IServiceCollection AddServiceBusClientAndProcessor(this IServiceCollection services,
                                                                  bool useManagedIdentity,
                                                                  string topicName,
                                                                  string subscriptionName,
                                                                  string serviceBusName,
                                                                  string serviceBusConnectionString
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

                ServiceBusSessionProcessorOptions processorOptions = new()
                {
                    AutoCompleteMessages = true,
                    MaxConcurrentSessions = 20,
                    MaxConcurrentCallsPerSession = 1
                };
                builder.AddClient<ServiceBusSessionProcessor, ServiceBusClientOptions>((_, _, provider) =>
                    provider
                        .GetRequiredService<ServiceBusClient>()
                        .CreateSessionProcessor(topicName, subscriptionName, processorOptions)
                )
                .WithName(subscriptionName);
            });

            return services;
        }
    }
}
