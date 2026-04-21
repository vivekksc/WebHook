using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Webhook.Utilities.Contracts
{
    public interface IProcessorService
    {
        Task StartProcessingAsync();

        Task StopProcessingAsync();

        Task ProcessMessageAsync(ServiceBusReceivedMessage message,
                                              ILogger logger,
                                              string databricksJobId,
                                              int databricksJobStatusPollingMaxWaitSeconds,
                                              string processorName,
                                              CancellationToken cancellationToken = default);
    }
}
