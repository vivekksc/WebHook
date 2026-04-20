using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Webhook.Utilities.Contracts;
using Webhook.Utilities.Utils;
using ExecutionContext = Microsoft.Azure.WebJobs.ExecutionContext;

namespace Webhook.Processor.Function
{
    public class Processor(
        IAzureClientFactory<ServiceBusClient> azSBClientFactory,
        IProcessorService processorService,
        EnvironmentVariables config
        )
    {
        private readonly EnvironmentVariables _config = config;
        private readonly ServiceBusClient _sbClient = azSBClientFactory.CreateClient(config.ServiceBusName);
        private readonly IProcessorService _processorService = processorService;

        [FunctionName("MDSProcessor-Update")]
        public async Task ProcessUpdatesAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger logger)
        {
            await ProcessMessages(logger,
                                  _config.ServiceBusTopic_MDS,
                                  _config.ServiceBusTopicSubscription_Update,
                                  funcContext.FunctionName);
        }

        [FunctionName("MDSProcessor-Comment")]
        public async Task ProcessCommentsAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger logger)
        {
            await ProcessMessages(logger,
                                  _config.ServiceBusTopic_MDS,
                                  _config.ServiceBusTopicSubscription_Comment,
                                  funcContext.FunctionName);
        }

        [FunctionName("MDSProcessor-Kudos")]
        public async Task ProcessKudosAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger logger)
        {
            await ProcessMessages(logger,
                                  _config.ServiceBusTopic_MDS,
                                  _config.ServiceBusTopicSubscription_Kudos,
                                  funcContext.FunctionName);
        }

        [FunctionName("MDSProcessor-Event")]
        public async Task ProcessEventAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger logger)
        {
            await ProcessMessages(logger,
                                  _config.ServiceBusTopic_MDS,
                                  _config.ServiceBusTopicSubscription_Event,
                                  funcContext.FunctionName);
        }

        [FunctionName("MDSProcessor-Bookmark")]
        public async Task ProcessBookmarkAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger logger)
        {
            await ProcessMessages(logger,
                                  _config.ServiceBusTopic_MDS,
                                  _config.ServiceBusTopicSubscription_Bookmark,
                                  funcContext.FunctionName);
        }

        [FunctionName("MDSProcessor-Article")]
        public async Task ProcessArticleAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger logger)
        {
            await ProcessMessages(logger,
                                  _config.ServiceBusTopic_MDS,
                                  _config.ServiceBusTopicSubscription_Article,
                                  funcContext.FunctionName);
        }

        private async Task ProcessMessages(ILogger logger, string topicName, string topicSubscriptionName, string processName)
        {
            // Build session processor options to mirror previous concurrency/prefetch behavior
            var options = new ServiceBusSessionProcessorOptions
            {
                MaxConcurrentSessions = _config.MaxConcurrentSessions,
                MaxConcurrentCallsPerSession = _config.MaxMessagesToProcessPerRun,
                PrefetchCount = _config.MaxMessagesToProcessPerRun,
                AutoCompleteMessages = false,
                // keep an auto-renewal window that covers expected processing + polling time
                MaxAutoLockRenewalDuration = TimeSpan.FromMilliseconds(_config.MaxWaitTimeForMessagesInMilliSeconds)
                                            + TimeSpan.FromSeconds(_config.DatabricksWorkflowJobStatusPollingMaxWait_Seconds_Ingest)
            };

            await using var processor = _sbClient.CreateSessionProcessor(topicName, topicSubscriptionName, options);

            processor.ProcessMessageAsync += async args =>
            {
                var message = args.Message;
                logger.LogInformation($"{processName} - Processing message {message.MessageId} in Session {args.SessionId}");

                try
                {
                    // Call existing service logic (unchanged). Pass the event cancellation token so service can react if needed.
                    await _processor_service.ProcessMessageAsync(
                        message,
                        logger,
                        _config.DatabricksWorkflowJobId_Ingest,
                        _config.DatabricksWorkflowJobStatusPollingMaxWait_Seconds_Ingest,
                        processName,
                        args.CancellationToken);

                    // Explicit completion to maintain same semantics as before
                    await args.CompleteMessageAsync(message);
                    logger.LogInformation($"{processName} - Completed message {message.MessageId} in Session {args.SessionId}");
                }
                catch (Exception ex)
                {
                    logger.LogError($"{processName} - Error processing message {message.MessageId} in Session {args.SessionId}: {ex.Message}");
                    try
                    {
                        await args.AbandonMessageAsync(message);
                        logger.LogInformation($"{processName} - Abandoned message {message.MessageId} in Session {args.SessionId}");
                    }
                    catch (Exception abandonEx)
                    {
                        logger.LogWarning($"{processName} - Failed to abandon message {message.MessageId}: {abandonEx.Message}");
                    }
                }
            };

            processor.ProcessErrorAsync += args =>
            {
                logger.LogError(args.Exception, $"{processName} - ServiceBus error (Entity: {args.EntityPath}, Namespace: {args.FullyQualifiedNamespace})");
                return Task.CompletedTask;
            };

            // Run processor for the same time-window previously used to AcceptNextSession
            var runTimeout = TimeSpan.FromMilliseconds(_config.MaxWaitTimeForMessagesInMilliSeconds);
            using var cts = new CancellationTokenSource(runTimeout);

            try
            {
                await processor.StartProcessingAsync(cts.Token);
                logger.LogInformation($"{processName} - Session processor started for subscription '{topicSubscriptionName}' (running for {runTimeout}).");

                // Wait until timeout elapses (or cancellation)
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
                }
                catch (TaskCanceledException) { /* expected on timeout */ }

                // Stop processing and allow running handlers to complete
                await processor.StopProcessingAsync();
                logger.LogInformation($"{processName} - Session processor stopped for subscription '{topicSubscriptionName}'.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"{processName} - Failed to start/stop session processor for subscription '{topicSubscriptionName}'.");
            }
        }
    }
}