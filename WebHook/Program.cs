//using Microsoft.AspNetCore.Builder;
//using Microsoft.Azure.Functions.Worker;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using Webhook.Utilities.Utils;

//var builder = WebApplication.CreateBuilder(args);

//var host = new HostBuilder()
//    .ConfigureFunctionsWebApplication()
//    //.ConfigureAppConfiguration(config =>
//    //{
//    //    // Add appsettings.json configuration
//    //    config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);

//    //    // Add environment variables configuration
//    //    config.AddEnvironmentVariables();
//    //})
//    .ConfigureServices((context, services) =>
//    {
//        services.AddApplicationInsightsTelemetryWorkerService();
//        services.ConfigureFunctionsApplicationInsights();

//        var configuration = context.Configuration;

//        // Register necessary services for WebhookListener
//        services.AddWebhookListenerServices(configuration);
//    })
//    .Build();

//host.Run();
