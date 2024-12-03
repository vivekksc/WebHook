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
            var context = builder.GetContext();
            var config = new ConfigurationBuilder()
                                    .SetBasePath(context.ApplicationRootPath)
                                    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                                    .AddEnvironmentVariables()
                                    .Build();
            builder.Services.AddSingleton<IConfiguration>(config);
            
            builder.Services.AddSingleton<EnvironmentVariables>(provider =>
            {
                var config = provider.GetService<IConfiguration>();
            
                // Bind configuration/environment variables
                return new()
                {
                    ServiceBusTopic = config.GetValue<string>("ServiceBus:Topic"),
                    ServiceBusTopicSubscription = config.GetValue<string>("ServiceBus:TopicSubscription")
                };
            });

            // Register Application services
            builder.Services.AddScoped<IPublisherService, PublisherService>();

            // Register ServiceBus instances
            _ = bool.TryParse(config["ServiceBus:UseManagedIdentity"], out bool useManagedIdentity);
            builder.Services.AddServiceBusClientAndSender(useManagedIdentity,
                                                        config["ServiceBus:Topic"],
                                                        config["ServiceBus:Name"],
                                                        config["ServiceBus:ConnectionString"]);
        }
    }
}
