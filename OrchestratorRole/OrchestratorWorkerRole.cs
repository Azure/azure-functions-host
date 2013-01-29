using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using DaasEndpoints;
using Executor;
using IndexDriver;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using Orchestrator;
using RunnerInterfaces;

namespace OrchestratorRole
{
    public class OrchestratorWorkerRole : RoleEntryPoint
    {
        string _localCacheRoot;
        DateTime _startTime;

        private IFunctionCompleteLogger _stats;
        private Services _services;
        private IFunctionInstanceLookup _lookup;

        // Check that the connection to the webservice is working.
        void CheckServiceUrl(IAccountInfo accountInfo)
        {
            string serviceUrl = accountInfo.WebDashboardUri;
            string uri = string.Format(@"{0}/Api/Execution/Heartbeat", serviceUrl);

            WebRequest request = WebRequest.Create(uri);
            request.Method = "GET";
            request.ContentType = "application/json";
            request.ContentLength = 0;

            var response = request.GetResponse(); // throws on errors and 404
        }

        public override void Run()
        {
            // This is a sample worker implementation. Replace with your logic.
            Trace.WriteLine("OrchestratorRole entry point called", "Information");
            
            IAccountInfo accountInfo = new AzureRoleAccountInfo();
            _services = new Services(accountInfo);


            try
            {                
                CheckServiceUrl(accountInfo);

                RunWorker();
            }
            catch (Exception e)
            {
                _services.LogFatalError("OrchError", e);
            }
        }
        
        void RunWorker()
        {

            _startTime = DateTime.UtcNow;
            _localCacheRoot = RoleEnvironment.GetLocalResource("localStore").RootPath;




            // This thread owns the function table. 
            Worker worker = null;

            _services.ResetHealthStatus();

            _stats = _services.GetStatsAggregator();
            _lookup = _services.GetFunctionInvokeLookup();

            var _statsBridge = _services.GetStatsAggregatorBridge();

            CancellationTokenSource cancelSource = new CancellationTokenSource();
            while (true)
            {
                // Service any requests from the queues. 
                // If any changes, then update (reinitialize) worker.
                if (PollForIndexRequest())
                {
                    if (worker != null)
                    {
                        worker.Dispose();
                    }
                    worker = null; // new entries, need to reinitialize 
                }

                if (worker == null)
                {
                    ResetExecutors();

                    worker = CreateWorker();
                    worker.Heartbeat.Uptime = _startTime;
                }

                _services.WriteHealthStatus(worker.Heartbeat);

                _statsBridge.DrainQueue(_stats, _lookup);
               
                // Polling walks all blobs. Could take a long time for a large container.
                worker.Poll(cancelSource.Token);                
                                
                // Delay before looping
                Thread.Sleep(1*1000);                
            }
        }

        private Worker CreateWorker()
        {
            IFunctionTable functionTable = _services.GetFunctionTable();

            IAccountInfo account = _services.AccountInfo;
            IFunctionUpdatedLogger logger = _services.GetFunctionInvokeLogger();
#if false
            // Based on AzureTasks
            TaskConfig taskConfig = GetAzureTaskConfig();
            IQueueFunction exec = new TaskExecutor(account, logger, taskConfig);
#else
            // Based on WorkerRoles (submitted via a Queue)
            var queue = _services.GetExecutionQueue();
            IQueueFunction exec = new WorkerRoleExecutionClient(queue, account, logger);            
#endif

            return new Orchestrator.Worker(functionTable, exec);
        }

        // Gets AzureTask configuration from the Azure config settings
        private TaskConfig GetAzureTaskConfig()
        {
            var taskConfig = new TaskConfig
            {
                TenantUrl = RoleEnvironment.GetConfigurationSettingValue("AzureTaskTenantUrl"),
                AccountName = RoleEnvironment.GetConfigurationSettingValue("AzureTaskAccountName"),
                Key = RoleEnvironment.GetConfigurationSettingValue("AzureTaskKey"),
                PoolName = RoleEnvironment.GetConfigurationSettingValue("AzureTaskPoolName")
            };
            return taskConfig;
        }
    
        private void ResetExecutors()
        {
            _services.ResetHealthStatus();

            string msg = string.Format("Reset at {0} by {1}", DateTime.Now, RoleEnvironment.CurrentRoleInstance.Id);
            _services.GetExecutorResetControlBlob().UploadText(msg);
        }

        bool PollForIndexRequest()
        {
            var queue = _services.GetOrchestratorControlQueue();

            var msg = queue.GetMessage();
            if (msg != null)
            {
                StringWriter swTempOutput = new StringWriter();

                string localPath = Path.Combine(_localCacheRoot, "index");
               
                try
                {
                    var result = Utility.ProcessExecute<IndexDriverInput, IndexResults>(
                        typeof(IndexDriver.Program),
                        localPath,
                        new IndexDriverInput
                        {
                             LocalCache = localPath,
                             Request = JsonCustom.DeserializeObject<IndexRequestPayload>(msg.AsString)
                        },
                        swTempOutput);                
                }
                finally
                {
                    queue.DeleteMessage(msg);
                    Utility.DeleteDirectory(localPath);                    
                }
                return true;
            }
            return false;
        }

        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections 
            ServicePointManager.DefaultConnectionLimit = 12;

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            return base.OnStart();
        }     
    }
}
