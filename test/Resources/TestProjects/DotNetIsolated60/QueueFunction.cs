using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DotNetIsolated60
{
    public class QueueFunction
    {
        private readonly ILogger _logger;

        public QueueFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<QueueFunction>();
        }

        [Function("QueueFunction")]
        public void Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string myQueueItem)
        {
            _logger.LogInformation($"C# Queue trigger function processed: {myQueueItem}");
        }
    }
}
