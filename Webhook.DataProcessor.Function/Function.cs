using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Webhook.DataProcessor.Function
{
    public class Function
    {
        private readonly ILogger<Function> _logger;

        public Function(ILogger<Function> logger)
        {
            _logger = logger;
        }

        [Function(nameof(Function))]
        public async Task Run(
            [ServiceBusTrigger("mytopic", "mysubscription", Connection = "sbConnStr")]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions)
        {
            _logger.LogInformation("Message ID: {id}", message.MessageId);
            _logger.LogInformation("Message Body: {body}", message.Body);
            _logger.LogInformation("Message Content-Type: {contentType}", message.ContentType);

             // Complete the message
            await messageActions.CompleteMessageAsync(message);
        }
    }
}
