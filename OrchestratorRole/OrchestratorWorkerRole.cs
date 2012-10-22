using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
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

        private ExecutionStatsAggregator _stats;

        public override void Run()
        {
            // This is a sample worker implementation. Replace with your logic.
            Trace.WriteLine("OrchestratorRole entry point called", "Information");

            _startTime = DateTime.UtcNow;
            _localCacheRoot = RoleEnvironment.GetLocalResource("localStore").RootPath;

            // This thread owns the function table. 
            var settings = Services.GetOrchestratorSettings();

            Worker worker = null;

            Services.ResetHealthStatus();

            _stats = Services.GetStatsAggregator();

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

                    worker = new Worker(settings);
                    worker.Heartbeat.Uptime = _startTime;
                }

                Services.WriteHealthStatus(worker.Heartbeat);

                UpdateStats();
               
                // Polling walks all blobs. Could take a long time for a large container.
                worker.Poll(cancelSource.Token);                
                                
                // Delay before looping
                Thread.Sleep(1*1000);                
            }
        }        

        // Aggregate the stats from any exuection instances that have completed. 
        void UpdateStats()
        {
            var queue = Secrets.GetExecutionCompleteQueue();
            while (true)
            {
                var msg = queue.GetMessage();
                if (msg == null)
                {
                    break;
                }

                queue.DeleteMessage(msg);

                var payload = JsonCustom.DeserializeObject<ExecutionFinishedPayload>(msg.AsString);
                foreach (var instance in payload.Instances)
                {
                    _stats.OnFunctionComplete(instance);
                }
            }

            _stats.Flush();
        }

        private void ResetExecutors()
        {
            Services.ResetHealthStatus();

            string msg = string.Format("Reset at {0} by {1}", DateTime.Now, RoleEnvironment.CurrentRoleInstance.Id);
            Secrets.GetExecutorResetControlBlob().UploadText(msg);
        }

        bool PollForIndexRequest()
        {
            var queue = Secrets.GetOrchestratorControlQueue();

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
