using System.Text.Json.Serialization;

namespace Webhook.Utilities.Models
{
    public class WebhookPayloadMetadata
    {
        public string Entity { get; set; }
        public string Action { get; set; }
        public int EntityId { get; set; }
        public string? CreatedAt { get; set; }
        public string? UpdatedAt { get; set; }
        public string? PublishedAt { get; set; }
    }
}
