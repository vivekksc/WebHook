using Webhook.Utilities.Models;

namespace Webhook.Utilities.Contracts
{
    public interface IPublisherService
    {
        /// <summary>
        /// Publishes payload to the ServiceBus Topic.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        Task PublishPayloadAsync(WebhookPayloadMetadata payloadMetadata, string payload);
    }
}
