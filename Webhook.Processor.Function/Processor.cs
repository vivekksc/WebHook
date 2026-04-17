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
            List<Task> sessionTasks = [];

            // Accept sessions and process them concurrently
            for (int i = 0; i < _config.MaxConcurrentSessions; i++)
            {
                CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMilliseconds(_config.MaxWaitTimeForMessagesInMilliSeconds));
                ServiceBusSessionReceiverOptions sessionReceiverOptions = new() { PrefetchCount = _config.MaxMessagesToProcessPerRun };
                ServiceBusSessionReceiver sessionReceiver;
                try
                {
                    sessionReceiver = await _sbClient.AcceptNextSessionAsync(topicName,
                                                                            topicSubscriptionName,
                                                                            sessionReceiverOptions,
                                                                            cancellationTokenSource.Token);
                }
                catch (TaskCanceledException) { break; } // No sessions available

                if (sessionReceiver == null) break; // No more sessions available

                try
                {
                    sessionTasks.Add(_processorService.ProcessSessionAsync(sessionReceiver,
                                                                           logger,
                                                                           _config.DatabricksWorkflowJobId_Ingest,
                                                                           _config.DatabricksWorkflowJobStatusPollingMaxWait_Seconds_Ingest,
                                                                           processName)); // Start processing in parallel
                }
                catch
                {
                    if (!sessionReceiver.IsClosed)
                        await sessionReceiver.CloseAsync();
                }
            }

            if (sessionTasks.Count > 0)
            {
                await Task.WhenAll(sessionTasks); // Wait for all sessions to complete
                logger.LogInformation($"{processName} - {sessionTasks.Count} sessions processed.");
            }

            logger.LogInformation($"{processName} - No sessions found.");
        }
    }
}