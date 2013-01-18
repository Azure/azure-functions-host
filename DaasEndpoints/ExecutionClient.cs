using System;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace DaasEndpoints
{
    // Execution client using WorkerRoles (submits to an Azure Queue that's picked up by a worker role).
    // Class for submitting a function to be executed.
    // This must enqueue the function, update the logging to mark that we have a function in queue. 
    public class WorkerRoleExecutionClient : IQueueFunction
    {
        private readonly IFunctionUpdatedLogger _logger;
        private readonly CloudQueue _queue;
        private readonly string _dashboardUri;

        public WorkerRoleExecutionClient(CloudQueue queue, IFunctionUpdatedLogger logger, string dashboardUri)
        {
            _logger = logger;
            _queue = queue;
            _dashboardUri = dashboardUri;
        }

        public ExecutionInstanceLogEntity Queue(FunctionInvokeRequest instance)
        {
            instance.Id = Guid.NewGuid(); // used for logging. 
            instance.ServiceUrl = _dashboardUri;

            // Log that the function is now queued.
            // Do this before queueing to avoid racing with execution 
            var logItem = new ExecutionInstanceLogEntity();
            logItem.FunctionInstance = instance;
            logItem.QueueTime = DateTime.UtcNow; // don't set starttime until a role actually executes it.

            _logger.Log(logItem);

            ExecutorClient.Queue(_queue, instance);

            // Now that it's queued, execution node may immediately pick up the queue item and start running it, 
            // and logging against it.

            return logItem;
        }
    }
}