using System;
using System.Collections.Generic;
using System.IO;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;
using System.Linq;
using Newtonsoft.Json;
using System.Threading;
using AzureTables;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure;

namespace DaasEndpoints
{
    // Despite the name, this is not an IOC container. 
    // This provides a global view of the distributed application (service, webpage, logging, tooling, etc)
    // Anything that needs an azure endpoint can go here.
    // This access the raw settings (especially account name) from Secrets, but then also provides the 
    // policy and references to stitch everything together. 
    public partial class Services
    {
        private readonly IAccountInfo _accountInfo;
        private readonly CloudStorageAccount _account;

        public Services(IAccountInfo accountInfo)
        {
            _accountInfo = accountInfo;
            _account = CloudStorageAccount.Parse(accountInfo.AccountConnectionString);
        }

        public CloudStorageAccount Account
        {
            get { return _account; }
        }

        public string AccountConnectionString
        {
            get { return _accountInfo.AccountConnectionString; }
        }

        // This blob is used by orchestrator to signal to all the executor nodes to reset.
        // THis is needed when orchestrator makes an update (like upgrading a funcioin) and needs 
        // the executors to clear their caches.
        public CloudBlob GetExecutorResetControlBlob()
        {
            CloudBlobClient client = _account.CreateCloudBlobClient();
            CloudBlobContainer c = client.GetContainerReference(EndpointNames.DaasControlContainerName);
            c.CreateIfNotExist();
            CloudBlob b = c.GetBlobReference("executor-reset");
            return b;
        }

        public CloudQueue GetExecutionCompleteQueue()
        {
            CloudQueueClient client = _account.CreateCloudQueueClient();
            var queue = client.GetQueueReference(EndpointNames.FunctionInvokeDoneQueue);
            queue.CreateIfNotExist();
            return queue;
        }

        public void PostDeleteRequest(FunctionInvokeRequest instance)
        {
            Utility.WriteBlob(_account, "abort-requests", instance.Id.ToString(), "delete requested");
        }

        public bool IsDeleteRequested(Guid instanceId)
        {
            bool x = Utility.DoesBlobExist(_account, "abort-requests", instanceId.ToString());
            return x;
        }

        public void LogFatalError(string message, Exception e)
        {
            StringWriter sw = new StringWriter();
            sw.WriteLine(message);
            sw.WriteLine(DateTime.Now);

            while (e != null)
            {
                sw.WriteLine("Exception:{0}", e.GetType().FullName);
                sw.WriteLine("Message:{0}", e.Message);
                sw.WriteLine(e.StackTrace);
                sw.WriteLine();
                e = e.InnerException;
            }

            string path = @"service.error\" + Guid.NewGuid().ToString() + ".txt";
            Utility.WriteBlob(_account, EndpointNames.ExecutionLogName, path, sw.ToString());
        }

        public void QueueIndexRequest(IndexRequestPayload payload)
        {
            string json = JsonCustom.SerializeObject(payload);
            GetOrchestratorControlQueue().AddMessage(new CloudQueueMessage(json));
        }
        
        public CloudQueue GetOrchestratorControlQueue()
        {
            CloudQueueClient client = _account.CreateCloudQueueClient();
            var queue = client.GetQueueReference(EndpointNames.OrchestratorControlQueue);
            queue.CreateIfNotExist();
            return queue;
        }

        // Important to get a quick, estimate of the depth of the execution queu. 
        public int? GetExecutionQueueDepth()
        {            
            var q = GetExecutionQueue();
            return q.RetrieveApproximateMessageCount();
        }
        
        public CloudQueue GetExecutionQueue()
        {
            CloudQueueClient queueClient = _account.CreateCloudQueueClient();
            var queue = queueClient.GetQueueReference(EndpointNames.ExecutionQueueName);
            queue.CreateIfNotExist();
            return queue;
        }

        public Worker GetOrchestrationWorker()
        {
            var settings = GetOrchestratorSettings();
            return new Orchestrator.Worker(settings);
        }

        public ExecutionInstanceLogEntity QueueExecutionRequest(FunctionInvokeRequest instance)
        {
            instance.Id = Guid.NewGuid(); // used for logging. 
            instance.ServiceUrl =  _accountInfo.WebDashboardUri;

            // Log that the function is now queued.
            // Do this before queueing to avoid racing with execution 
            IFunctionUpdatedLogger logger = GetFunctionInvokeLogger();

            var logItem = new ExecutionInstanceLogEntity();
            logItem.FunctionInstance = instance;
            logItem.QueueTime = DateTime.UtcNow; // don't set starttime until a role actually executes it.

            logger.Log(logItem);

            ExecutorClient.Queue(GetExecutionQueue(), instance);

            // Now that it's queued, execution node may immediately pick up the queue item and start running it, 
            // and logging against it.

            return logItem;
        }

        public OrchestratorSettings GetOrchestratorSettings()
        {
            return new OrchestratorSettings(this);
        }

        public AzureTable<BinderEntry> GetBinderTable()
        {
            return new AzureTable<BinderEntry>(_account, EndpointNames.BindersTableName);
        }

        public CloudBlobContainer GetExecutionLogContainer()
        {
            CloudBlobClient client = _account.CreateCloudBlobClient();
            CloudBlobContainer c = client.GetContainerReference(EndpointNames.ExecutionLogName);
            c.CreateIfNotExist();
            return c;
        }
    }
}
