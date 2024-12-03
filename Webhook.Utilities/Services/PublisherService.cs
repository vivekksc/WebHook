using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Webhook.Utilities.Contracts;
using Webhook.Utilities.Models;
using Webhook.Utilities.Utils;

namespace Webhook.Utilities.Services
{
    public class PublisherService(IAzureClientFactory<ServiceBusSender> serviceBusSenderFactory, EnvironmentVariables config) : IPublisherService
    {
        private readonly ServiceBusSender _sender = serviceBusSenderFactory.CreateClient(config.ServiceBusTopic);

        public async Task PublishPayloadAsync(WebhookPayloadMetadata payloadMetadata, string payload)
        {
            // Prepare message
            ServiceBusMessage message = new(payload)
            {
                SessionId = payloadMetadata.EntityId.ToString(),
                Subject = payloadMetadata.Entity
            };
            message.ApplicationProperties["CreatedAt"] = payloadMetadata.CreatedAt;
            message.ApplicationProperties["PublishedAt"] = payloadMetadata.PublishedAt;
            message.ApplicationProperties["UpdatedAt"] = payloadMetadata.UpdatedAt;

            // Send message
            await _sender.SendMessageAsync(message).ConfigureAwait(false);
        }
    }
}
