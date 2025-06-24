namespace Webhook.Utilities.Contracts
{
    public interface IProcessorService
    {
        Task StartProcessingAsync();

        Task StopProcessingAsync();
    }
}
