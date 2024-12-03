using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using Webhook.Utilities.Utils;

namespace Webhook.DataProcessor.Function
{
    class DataProcessor(ILogger<DataProcessor> logger)
    {
        private readonly ILogger<DataProcessor> _logger = logger;
        private static readonly HttpClient client = new();

        [Function("DataProcessor")]
        public async Task Run([ServiceBusTrigger(topicName:"%ServiceBus:Topic%",
                                                 subscriptionName:"%ServiceBus:TopicSubscription%",
                                                 Connection = "%ServiceBus:ConnectionString%")]
            ServiceBusReceivedMessage message)
        {
            string eventPayload = Encoding.UTF8.GetString(message.Body.ToArray());

            var payload = new
            {
                job_id = EnvironmentVariables.DatabricksWorkflowJobId_Ingest,
                job_parameters = new
                {
                    payload = CompressAndBase64Encode(eventPayload)
                }
            };

            var jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", EnvironmentVariables.DatabricksAccessToken);

            HttpResponseMessage response = await client.PostAsync($"https://{EnvironmentVariables.DatabricksInstance}/api/2.1/jobs/run-now", content);

            string responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation(responseContent);
        }

        public static string CompressAndBase64Encode(string jsonString)
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
