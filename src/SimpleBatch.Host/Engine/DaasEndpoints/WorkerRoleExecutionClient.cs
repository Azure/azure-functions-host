using Executor;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace DaasEndpoints
{
    // Execution client using WorkerRoles (submits to an Azure Queue that's picked up by a worker role).
    // Class for submitting a function to be executed.
    // This must enqueue the function, update the logging to mark that we have a function in queue. 
    // Most of the real work here is the Azure worker role that's listening on the queue. 
    internal class WorkerRoleExecutionClient : QueueFunctionBase
    {
        private readonly CloudQueue _queue;

        public WorkerRoleExecutionClient(CloudQueue queue, QueueInterfaces interfaces)
            : base(interfaces)
        {
            _queue = queue;
        }

        protected override void Work(ExecutionInstanceLogEntity logItem)
        {
            ExecutorClient.Queue(_queue,  logItem.FunctionInstance);
        }
    }
}