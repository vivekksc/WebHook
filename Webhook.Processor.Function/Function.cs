using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Webhook.Processor.Function
{
    public class Function
    {
        [FunctionName("Function")]
        public void Run([ServiceBusTrigger("mytopic", "mysubscription", Connection = "")]string mySbMsg, ILogger log)
        {
            log.LogInformation($"C# ServiceBus topic trigger function processed message: {mySbMsg}");
        }
    }
}
