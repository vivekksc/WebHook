using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Webhook.Utilities.Contracts;
using Webhook.Utilities.Models;

namespace Webhook.Listener.Function
{
    public class WebhookListener(ILogger<WebhookListener> logger, IPublisherService publisherService)
    {
        private readonly ILogger<WebhookListener> _logger = logger;
        private readonly IPublisherService _publisherService = publisherService;

        [FunctionName("WebhookListener")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Post))] HttpRequest req)
        {
            try
            {
                string payload = await new StreamReader(req.Body).ReadToEndAsync();
                var payloadMetadata = HandlePayload(payload);

                await _publisherService.PublishPayloadAsync(payloadMetadata, payload);

                return new AcceptedResult();
            }
            catch (Exception ex)
            {
                var exceptionDetails = $"{ex.Message} {ex.Source} {ex.InnerException} {ex.StackTrace}";
                _logger.LogError(exceptionDetails);
                return new ObjectResult(exceptionDetails) { StatusCode = (int)HttpStatusCode.InternalServerError };
            }
        }

        private static WebhookPayloadMetadata HandlePayload(string jsonPayload)
        {
            WebhookPayloadMetadata webhookPayloadMetadata = new();
            using (JsonDocument document = JsonDocument.Parse(jsonPayload))
            {
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("action", out JsonElement actionElement))
                {
                    webhookPayloadMetadata = new()
                    {
                        Entity = actionElement.GetString().Split(".")[0],
                        Action = actionElement.GetString().Split(".")[1]
                    };

                    switch (webhookPayloadMetadata.Entity)
                    {
                        case "article":
                        case "bookmark":
                        case "event":
                        case "kudos":
                            if (root.TryGetProperty(webhookPayloadMetadata.Entity, out JsonElement entityElement) &&
                                entityElement.TryGetProperty("id", out JsonElement entityIdElement))
                            {
                                webhookPayloadMetadata.EntityId = entityIdElement.GetInt32();
                                webhookPayloadMetadata.CreatedAt = entityElement.TryGetProperty("created_at", out JsonElement CreatedAt)
                                                                    ? CreatedAt.GetString()
                                                                    : default;
                                webhookPayloadMetadata.PublishedAt = entityElement.TryGetProperty("published_at", out JsonElement PublishedAt)
                                                                    ? PublishedAt.GetString()
                                                                    : default;
                                webhookPayloadMetadata.UpdatedAt = entityElement.TryGetProperty("updated_at", out JsonElement UpdatedAt)
                                                                    ? UpdatedAt.GetString()
                                                                    : default;
                            }
                            break;

                        case "comment":
                            if (root.TryGetProperty(webhookPayloadMetadata.Entity, out JsonElement commentElement) &&
                                commentElement.TryGetProperty("comment_id", out JsonElement commentIdElement))
                            {
                                webhookPayloadMetadata.EntityId = commentIdElement.GetInt32();
                                webhookPayloadMetadata.CreatedAt = commentElement.TryGetProperty("created_at", out JsonElement CreatedAt)
                                                                    ? CreatedAt.GetString()
                                                                    : default;
                                webhookPayloadMetadata.PublishedAt = commentElement.TryGetProperty("published_at", out JsonElement PublishedAt)
                                                                    ? PublishedAt.GetString()
                                                                    : default;
                                webhookPayloadMetadata.UpdatedAt = commentElement.TryGetProperty("updated_at", out JsonElement UpdatedAt)
                                                                    ? UpdatedAt.GetString()
                                                                    : default;
                            }
                            break;

                        default:
                            break;
                    }
                }
                else
                {
                    // TODO: Check if missing info to be notified to sender
                    Console.WriteLine("Invalid payload: Missing action.");
                }
            }

            return webhookPayloadMetadata;
        }
    }
}
