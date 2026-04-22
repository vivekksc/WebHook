using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Webhook.Utilities.Contracts;
using Webhook.Utilities.Models;
using Webhook.Utilities.Utils;

namespace Webhook.Utilities.Services
{
    public class ProcessorService(
        IHttpClientFactory httpClientFactory,
        EnvironmentVariables config) : IProcessorService
    {
        private readonly EnvironmentVariables _config = config;
        private readonly HttpClient httpClient = httpClientFactory.CreateClient();

        public async Task ProcessSessionAsync(ServiceBusSessionReceiver sessionReceiver,
                                              ILogger logger,
                                              string databricksJobId,
                                              int databricksJobStatusPollingMaxWaitSeconds,
                                              string processorName)
        {
            logger.LogInformation($"{processorName} - Processing session: {sessionReceiver.SessionId}");
            while (true)
            {
                // Fetch messages in order within the session
                IReadOnlyList<ServiceBusReceivedMessage> messages =
                    await sessionReceiver.ReceiveMessagesAsync(maxMessages: _config.MaxMessagesPerSession, TimeSpan.FromMilliseconds(_config.MaxWaitTimeForMessagesInMilliSeconds));

                if (messages.Count == 0) break; // No more messages in session

                foreach (var message in messages)
                {
                    logger.LogInformation($"{processorName} - Processing message {message.MessageId} in Session {sessionReceiver.SessionId}");

                    try
                    {
                        await ProcessMessageAsync(message, logger, databricksJobId, databricksJobStatusPollingMaxWaitSeconds, processorName);
                        await sessionReceiver.CompleteMessageAsync(message); // Ensure ordered completion
                    }
                    catch
                    {
                        await sessionReceiver.AbandonMessageAsync(message); // Abandon message to attempt further delivery if configured in SB subscription.
                    }
                }
            }

            await sessionReceiver.CloseAsync(); // Close session receiver after processing
        }

        // Added optional CancellationToken parameter so callers (session processor) can pass handler cancellation tokens.
        public async Task ProcessMessageAsync(ServiceBusReceivedMessage message,
                                              ILogger logger,
                                              string databricksJobId,
                                              int databricksJobStatusPollingMaxWaitSeconds,
                                              string processorName,
                                              CancellationToken cancellationToken = default)
        {
            using MemoryStream eventPayloadStream = new(message.Body.ToArray());
            string eventEntity = message.Subject;
            string eventId = message.SessionId;
            string logDetail = $"Entity: {eventEntity}, EntityId: {eventId}";

            try
            {
                string blobName = $"{eventId}_{message.MessageId}_{processorName}.json";
                var uploadResponse = await UploadBlobAsync(eventPayloadStream, blobName);
                logger.LogInformation($"{processorName} - {logDetail}, PayloadBlobUploadStatus: {uploadResponse.ReasonPhrase}");

                var payload = new
                {
                    job_id = databricksJobId,
                    job_parameters = new
                    {
                        entity = eventEntity,
                        payloadFileRelativePath = uploadResponse.BlobRelativePath,
                        purgePayloadPostProcess = "true"
                    }
                };

                var jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.DatabricksAccessToken);

                HttpResponseMessage response = await httpClient.PostAsync($"https://{_config.DatabricksInstance}/api/2.1/jobs/run-now", content);
                string responseContent = await response.Content.ReadAsStringAsync();
                logDetail = $"{processorName} - {logDetail}, IngestionResponse: {responseContent}";
                logger.LogInformation(logDetail);
                response.EnsureSuccessStatusCode();

                JsonDocument responseJson = JsonDocument.Parse(responseContent);
                var isRunIdFound = responseJson.RootElement.GetProperty("run_id").TryGetInt64(out long dbxJobRunId);
                if (isRunIdFound)
                    await WaitForJobCompletionAsync(logger, dbxJobRunId, databricksJobStatusPollingMaxWaitSeconds, processorName, cancellationToken);
                else
                    await Task.Delay(TimeSpan.FromSeconds(databricksJobStatusPollingMaxWaitSeconds), cancellationToken);

                //var deleteResponse = await DeleteBlobAsync(blobName);
                //logger.LogInformation($"{processorName} - {logDetail}, PayloadBlobDeleteStatus: {deleteResponse.ReasonPhrase}");
            }
            catch (Exception ex)
            {
                string exceptionDetails = $"{processorName} - {logDetail} | ExceptionMessage: {ex.Message} | InnerException: {ex.InnerException} | StackTrace: {ex.StackTrace}";
                logger.LogError(exceptionDetails);
                throw new WebException(exceptionDetails);
            }
        }

        // Updated to use the newer Databricks run-get endpoint and to evaluate status via status.state and status.termination_details
        public async Task<bool> WaitForJobCompletionAsync(ILogger logger,
                                                          long jobRunId,
                                                          int jobStatusPollingMaxWaitSeconds,
                                                          string processorName,
                                                          CancellationToken cancellationToken = default)
        {
            string url = $"https://{_config.DatabricksInstance}/api/2.2/jobs/runs/get?run_id={jobRunId}";
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.DatabricksAccessToken);

            DateTime startTime = DateTime.Now;
            TimeSpan maxWaitTime = TimeSpan.FromSeconds(jobStatusPollingMaxWaitSeconds);

            while (DateTime.Now - startTime < maxWaitTime)
            {
                try
                {
                    HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode(); // Throw exception if not 2XX

                    string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogInformation(responseBody);

                    using JsonDocument json = JsonDocument.Parse(responseBody);
                    var root = json.RootElement;

                    // Navigate to the status object
                    if (!root.TryGetProperty("status", out JsonElement jobStatus))
                    {
                        // If not exists, log and wait then retry
                        logger.LogInformation($"{processorName} - No status element found in job run response. Retrying...");
                        await Task.Delay(TimeSpan.FromSeconds(_config.DatabricksWorkflowJobStatusPollingDelay_Seconds), cancellationToken);
                        continue;
                    }

                    string jobState = jobStatus.TryGetProperty("state", out JsonElement state) ? state.GetString() : null;

                    // termination_details may be absent until TERMINATED
                    string? jobStatusTerminationDetailsCode = default;
                    string? jobStatusTerminationDetailsType = default;
                    if (jobStatus.TryGetProperty("termination_details", out JsonElement termEl) && termEl.ValueKind == JsonValueKind.Object)
                    {
                        if (termEl.TryGetProperty("code", out JsonElement jobStatusCode) && jobStatusCode.ValueKind != JsonValueKind.Null)
                            jobStatusTerminationDetailsCode = jobStatusCode.GetString();

                        if (termEl.TryGetProperty("type", out JsonElement jobStatusType) && jobStatusType.ValueKind != JsonValueKind.Null)
                            jobStatusTerminationDetailsType = jobStatusType.GetString();
                    }

                    // Decide based on status.state and termination_details.code/type
                    if (string.Equals(jobState, "TERMINATED", StringComparison.OrdinalIgnoreCase))
                    {
                        // If termination details indicate success, return true; otherwise throw
                        if ((!string.IsNullOrWhiteSpace(jobStatusTerminationDetailsCode) && jobStatusTerminationDetailsCode.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrWhiteSpace(jobStatusTerminationDetailsType) && jobStatusTerminationDetailsType.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)))
                        {
                            logger.LogInformation($"{processorName} - Job {jobRunId} terminated with SUCCESS.");
                            return true;
                        }

                        // Not a successful termination
                        throw new Exception($"{processorName} - Job {jobRunId} terminated with code:{jobStatusTerminationDetailsCode ?? string.Empty} type:{jobStatusTerminationDetailsType ?? string.Empty}. Details - {responseBody}");
                    }
                    else
                    {
                        // Job still running or in other progress state
                        logger.LogInformation($"{processorName} - Waiting for job {jobRunId} (status: {jobState}) to finish...");
                        await Task.Delay(TimeSpan.FromSeconds(_config.DatabricksWorkflowJobStatusPollingDelay_Seconds), cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning($"{processorName} - Job status polling cancelled for job {jobRunId}.");
                    throw;
                }
                catch (Exception)
                {
                    // Surfacing exceptions to caller
                    throw;
                }
            }

            Console.WriteLine($"{processorName} - Timeout reached! Proceeding even though job {jobRunId} is still running.");
            return true;  // Force return `true` after timeout
        }


        private async Task<BlobUploadResponse> UploadBlobAsync(MemoryStream blobContent, string blobName)
        {
            var blobEndpoint = new Uri($"https://{_config.StorageAccountName}.blob.core.windows.net/{_config.StorageAccountContainer_Ingestion}");
            BlobContainerClient containerClient;

            if (_config.StorageAccount_UseManagedIdentity)
            {
                var credential = new DefaultAzureCredential();
                containerClient = new BlobContainerClient(blobEndpoint, credential);
            }
            else
            {
                containerClient = new BlobContainerClient(_config.StorageAccount_ConnectionString, _config.StorageAccountContainer_Ingestion);
            }

            await containerClient.CreateIfNotExistsAsync();
            var blockBlobClient = containerClient.GetBlockBlobClient(blobName);

            var result = await blockBlobClient.UploadAsync(blobContent);
            var response = result.GetRawResponse();

            string uploadResponse = default;
            var responseStream = response?.ContentStream;
            if (responseStream != null)
            {
                responseStream.Seek(0, SeekOrigin.Begin);
                using StreamReader reader = new(responseStream);
                uploadResponse = reader.ReadToEndAsync().Result;
            }

            return new BlobUploadResponse
            {
                IsSuccess = !response.IsError,
                Status = response.Status,
                ReasonPhrase = response.ReasonPhrase,
                Content = uploadResponse,
                BlobURI = blockBlobClient.Uri.AbsoluteUri,
                BlobRelativePath = blockBlobClient.Uri.AbsolutePath
            };
        }

        private async Task<BlobUploadResponse> DeleteBlobAsync(string blobName)
        {
            var blobEndpoint = new Uri($"https://{_config.StorageAccountName}.blob.core.windows.net/{_config.StorageAccountContainer_Ingestion}");
            BlobContainerClient containerClient;

            if (_config.StorageAccount_UseManagedIdentity)
            {
                var credential = new DefaultAzureCredential();
                containerClient = new BlobContainerClient(blobEndpoint, credential);
            }
            else
            {
                containerClient = new BlobContainerClient(_config.StorageAccount_ConnectionString, _config.StorageAccountContainer_Ingestion);
            }

            await containerClient.CreateIfNotExistsAsync();
            var blockBlobClient = containerClient.GetBlockBlobClient(blobName);

            var result = await blockBlobClient.DeleteIfExistsAsync();
            var response = result.GetRawResponse();

            string deleteResponse = default;
            var responseStream = response?.ContentStream;
            if (responseStream != null)
            {
                responseStream.Seek(0, SeekOrigin.Begin);
                using StreamReader reader = new(responseStream);
                deleteResponse = reader.ReadToEndAsync().Result;
            }

            return new BlobUploadResponse
            {
                IsSuccess = !response.IsError,
                Status = response.Status,
                ReasonPhrase = response.ReasonPhrase,
                Content = deleteResponse,
                BlobURI = blockBlobClient.Uri.AbsoluteUri,
                BlobRelativePath = blockBlobClient.Uri.AbsolutePath
            };
        }

        private static string CompressAndBase64Encode(string jsonString)
        {
            string serializedJsonString = JsonConvert.SerializeObject(JToken.Parse(jsonString), Formatting.None);

            // Convert the JSON string to bytes
            byte[] jsonBytes = Encoding.UTF8.GetBytes(serializedJsonString);

            // Compress the bytes using Gzip
            using (var outputStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(outputStream, CompressionLevel.SmallestSize))
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

    public sealed class BlobUploadResponse()
    {
        public bool IsSuccess { get; set; }
        public int Status { get; set; }
        public string ReasonPhrase { get; set; }
        public string Content { get; set; }
        public string BlobURI { get; set; }
        public string BlobRelativePath { get; set; }
    }
}