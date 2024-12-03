using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Webhook.Utilities.Contracts;
using Webhook.Utilities.Services;
using Webhook.Utilities.Utils;

[assembly: FunctionsStartup(typeof(Webhook.Listener.Function.Startup))]
namespace Webhook.Listener.Function
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            //// Load configuration from environment variables
            //var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();

            //// Bind configuration to AppConfig static class
            //EnvironmentVariables.ServiceBusTopic = Environment.GetEnvironmentVariable("ServiceBus:Topic");
            //EnvironmentVariables.ServiceBusTopicSubscription = Environment.GetEnvironmentVariable("ServiceBus:TopicSubscription");

            builder.Services.AddSingleton<EnvironmentVariables>(provider =>
                {
                    var config = provider.GetService<IConfiguration>();

                    // Bind configuration/environment variables
                    return new()
                    {
                        ServiceBusTopic = Environment.GetEnvironmentVariable("ServiceBus:Topic"),
                        ServiceBusTopicSubscription = Environment.GetEnvironmentVariable("ServiceBus:TopicSubscription")
                    };
                });

            // Register Application services
            builder.Services.AddScoped<IPublisherService, PublisherService>();

            // Register ServiceBus instances
            _ = bool.TryParse(Environment.GetEnvironmentVariable("ServiceBus:UseManagedIdentity"), out bool useManagedIdentity);
            builder.Services.AddServiceBusClientAndSender(useManagedIdentity,
                                                        Environment.GetEnvironmentVariable("ServiceBus:Topic"),
                                                        Environment.GetEnvironmentVariable("ServiceBus:Name"),
                                                        Environment.GetEnvironmentVariable("ServiceBus:ConnectionString"));
        }
    }
}
