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

        private ExecutionStatsAggregatorBridge _statsBridge;

        private IPrereqManager _prereqManager;
        private IActivateFunction _activator;

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

            var qi = _services.GetQueueInterfaces();

            _stats = _services.GetFunctionCompleteLogger();            
            _statsBridge = _services.GetStatsAggregatorBridge();

            _activator = _services.GetActivator(qi);
            _lookup = qi.Lookup;
            _prereqManager = qi.PreqreqManager;

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

                DrainCompletionQueue();

                // Polling walks all blobs. Could take a long time for a large container.
                worker.Poll(cancelSource.Token);

                // Delay before looping
                Thread.Sleep(1 * 1000);
            }
        }

        private void DrainCompletionQueue()
        {            
            foreach (Guid instance in _statsBridge.DrainQueue())
            {
                ExecutionInstanceLogEntity func = _lookup.Lookup(instance);
                _stats.IndexCompletedFunction(func);

                // $$$ This could be moved to the execution nodes; but then nodes would need a way
                // to directly queue. 
                _prereqManager.OnComplete(instance, _activator);
            }
            _stats.Flush();
        }

        private Worker CreateWorker()
        {
            IFunctionTable functionTable = _services.GetFunctionTable();
            IQueueFunction exec = _services.GetQueueFunction();

            return new Orchestrator.Worker(functionTable, exec);
        }
    
        private void ResetExecutors()
        {
            _services.ResetHealthStatus();

            string msg = string.Format("Reset at {0} by {1}", DateTime.Now, RoleEnvironment.CurrentRoleInstance.Id);
            _services.GetExecutorResetControlBlob().UploadText(msg);
        }

        // Ping the given URL for new functions to be indexed
        private void PollKudu(string url, TextWriter output)
        {
            output.WriteLine("indexing functions at: {0}", url);
            try
            {
                var funcs = Utility.GetJson<FunctionDefinition[]>(url);

                IFunctionTable table = _services.GetFunctionTable();

                // Remove stale functions that are at the same URL. 
                {
                    var listDelete = new List<FunctionDefinition>();
                    foreach (var func in table.ReadAll())
                    {
                        var loc = func.Location as IUrlFunctionLocation;
                        if (loc != null)
                        {
                            if (string.Compare(loc.InvokeUrl, url, StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                listDelete.Add(func);
                            }
                        }
                    }
                    foreach (var func in listDelete)
                    {
                        // $$$ Log deleted functions. Share logic here with indexing
                        table.Delete(func);
                    }
                }


                output.WriteLine("Adding/Refreshing {0} functions:", funcs.Length);
                foreach (var func in funcs)
                {
                    output.WriteLine(func.Location.GetShortName());
                    table.Add(func);
                }

                output.WriteLine("DONE: SUCCESS");
            }
            catch(Exception e)
            {
                output.WriteLine("Failure to index!");
                output.WriteLine("Exception {0}, {1}", e.GetType().FullName, e.Message);
                output.WriteLine(e.StackTrace);

                output.WriteLine("DONE: FAILED");
            }
        }

        bool PollForIndexRequest()
        {
            var queue = _services.GetOrchestratorControlQueue();

            var msg = queue.GetMessage();
            if (msg != null)
            {
                IndexRequestPayload payload = JsonCustom.DeserializeObject<IndexRequestPayload>(msg.AsString);

                StringWriter swTempOutput = new StringWriter();

                string localPath = Path.Combine(_localCacheRoot, "index");
               
                try
                {
                    IndexUrlOperation urlOp = payload.Operation as IndexUrlOperation;
                    if (urlOp != null)
                    {
                        PollKudu(urlOp.Url, swTempOutput);

                        var urlLogger = payload.Writeback;
                        if (urlLogger != null)
                        {
                            Console.WriteLine("Logging output to: {0}", urlLogger);
                            CloudBlob blob = new CloudBlob(urlLogger);
                            blob.UploadText(swTempOutput.ToString());
                        }

                        return true;
                    }

                    var result = Utility.ProcessExecute<IndexDriverInput, IndexResults>(
                        typeof(IndexDriver.Program),
                        localPath,
                        new IndexDriverInput
                        {
                             LocalCache = localPath,
                             Request = payload
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
