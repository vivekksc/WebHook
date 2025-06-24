using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Amqp.Framing;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Azure;
using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Webhook.Utilities.Utils;

namespace Webhook.Processor.Function
{
    public class Processor(
        //IProcessorService processorService,
        IAzureClientFactory<ServiceBusReceiver> azClientFactory,
        IHttpClientFactory httpClientFactory,
        EnvironmentVariables config
        )
    {
        private readonly EnvironmentVariables _config = config;
        //private readonly IProcessorService _processorService = processorService;
        private readonly ServiceBusReceiver _receiver = azClientFactory.CreateClient(config.ServiceBusTopicSubscription);
        private readonly HttpClient httpClient = httpClientFactory.CreateClient();

        //[Disable]
        //[FunctionName("Processor")]
        //public async Task Run([TimerTrigger("*/5 * * * * *")] TimerInfo myTimer)
        //{
        //    try
        //    {
        //        //// Handle the ProcessSessionMessageAsync event
        //        //_processor.ProcessMessageAsync += async args =>
        //        //{
        //        //    try
        //        //    {
        //        //        // Process the message
        //        //        string eventPayload = Encoding.UTF8.GetString(args.Message.Body.ToArray());

        //        //        var payload = new
        //        //        {
        //        //            job_id = _config.DatabricksWorkflowJobId_Ingest,
        //        //            job_parameters = new
        //        //            {
        //        //                payload = CompressAndBase64Encode(eventPayload)
        //        //            }
        //        //        };

        //        //        var jsonPayload = JsonConvert.SerializeObject(payload);
        //        //        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        //        //        httpClient.DefaultRequestHeaders.Clear();
        //        //        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.DatabricksAccessToken);

        //        //        HttpResponseMessage response = await httpClient.PostAsync($"https://{_config.DatabricksInstance}/api/2.1/jobs/run-now", content);
        //        //        //string responseContent = await response.Content.ReadAsStringAsync();

        //        //        // Complete the message
        //        //        await args.CompleteMessageAsync(args.Message);
        //        //    }
        //        //    catch (Exception ex)
        //        //    {
        //        //        // Abandon the message if there's an error
        //        //        //await args.AbandonMessageAsync(args.Message);
        //        //    }
        //        //};

        //        //// Handle the ProcessErrorAsync event
        //        //_processor.ProcessErrorAsync += async args =>
        //        //{
        //        //    await Task.CompletedTask;
        //        //};

        //        // Start processing
        //        await _processorService.StartProcessingAsync();

        //        // Wait for a short time to ensure messages are processed
        //        await Task.Delay(TimeSpan.FromSeconds(10));

        //        // Stop processing
        //        await _processorService.StopProcessingAsync();
        //    }
        //    catch (Exception ex)
        //    {

        //    }

        //}

        [FunctionName("ProcessorV2")]
        public async Task RunV2([TimerTrigger("*/5 * * * * *")] TimerInfo myTimer)
        {
            var messages = await _receiver.ReceiveMessagesAsync(20);
            foreach (var message in messages)
            {
                try
                {
                    // Process the message
                    string eventPayload = Encoding.UTF8.GetString(message.Body.ToArray());

                    var payload = new
                    {
                        job_id = _config.DatabricksWorkflowJobId_Ingest,
                        job_parameters = new
                        {
                            payload = CompressAndBase64Encode(eventPayload)
                        }
                    };

                    var jsonPayload = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    httpClient.DefaultRequestHeaders.Clear();
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.DatabricksAccessToken);

                    HttpResponseMessage response = await httpClient.PostAsync($"https://{_config.DatabricksInstance}/api/2.1/jobs/run-now", content);

                    //Complete Message
                    await _receiver.CompleteMessageAsync(message).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Task.Delay()
                }
            }
        }

        private static string CompressAndBase64Encode(string jsonString)
        {
            // Convert the JSON string to bytes
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);

            // Compress the bytes using Gzip
            using (var outputStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
                {
                    gzipStream.Write(jsonBytes, 0, jsonBytes.Length);
                }

                // Get the compressed bytes
                byte[] compressedBytes = outputStream.ToArray();

                // Encode the compressed bytes to base64
                string base64String = Convert.ToBase64String(compressedBytes);
                return base64String;
            }
        }
    }
}
