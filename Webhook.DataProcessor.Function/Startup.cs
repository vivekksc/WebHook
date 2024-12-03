using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Webhook.Utilities.Utils;

[assembly: FunctionsStartup(typeof(Webhook.DataProcessor.Function.Startup))]
namespace Webhook.DataProcessor.Function
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
                    DatabricksInstance = config.GetValue<string>("Databricks:Instance"),
                    DatabricksAccessToken = config.GetValue<string>("Databricks:AccessToken"),
                    DatabricksWorkflowJobId_Ingest = config.GetValue<string>("Databricks:WorkflowJobId_Ingest")
                };
            });
        }
    }
}
