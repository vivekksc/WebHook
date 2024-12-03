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
            // Load configuration from environment variables
            var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();

            // Bind configuration to AppConfig static class
            EnvironmentVariables.DatabricksInstance = config.GetValue<string>("Databricks:Instance");
            EnvironmentVariables.DatabricksAccessToken = config.GetValue<string>("Databricks:AccessToken");
            EnvironmentVariables.DatabricksWorkflowJobId_Ingest = config.GetValue<string>("Databricks:WorkflowJobId_Ingest");
        }
    }
}
