using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using Webhook.Utilities.Contracts;
using Webhook.Utilities.Utils;

namespace Webhook.Utilities.Services
{
    public class ProcessorService : IProcessorService
    {
        private readonly ServiceBusSessionProcessor _processor;
        private readonly EnvironmentVariables _config;
        private readonly HttpClient httpClient;

        public ProcessorService(IAzureClientFactory<ServiceBusSessionProcessor> azClientFactory,
                                IHttpClientFactory httpClientFactory,
                                EnvironmentVariables config)
        {
            _config = config;
            httpClient = httpClientFactory.CreateClient();
            
            _processor = azClientFactory.CreateClient(config.ServiceBusTopicSubscription);
            _processor.ProcessMessageAsync += ProcessSessionMessageAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;
        }

        public async Task StartProcessingAsync()
        {
            await _processor.StartProcessingAsync();
        }

        public async Task StopProcessingAsync()
        {
            await _processor.StopProcessingAsync();
        }

        private async Task ProcessSessionMessageAsync(ProcessSessionMessageEventArgs args)
        {
            try
            {
                // Process the message
                string eventPayload = Encoding.UTF8.GetString(args.Message.Body.ToArray());
                
                await ProcessPayloadAsync(eventPayload);

                //// Complete the message
                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                //Abandon the message if there's an error
                await args.AbandonMessageAsync(args.Message);
            }
        }

        private async Task ProcessPayloadAsync(string eventPayload)
        {
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
            //string responseContent = await response.Content.ReadAsStringAsync();
        }

        private Task ProcessErrorAsync(ProcessErrorEventArgs args)
        {
            return Task.CompletedTask;
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