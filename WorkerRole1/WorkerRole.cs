using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace WorkerRole1
{
    public class WorkerRole : RoleEntryPoint
    {
        DateTime _startTime;
        DateTime _lastResetTime;

        

        public override void Run()
        {
            _startTime = DateTime.UtcNow;
            
            // This is a sample worker implementation. Replace with your logic.
            Trace.WriteLine("WorkerRole1 entry point called", "Information");

            var local = RoleEnvironment.GetLocalResource("localStore");

            var settings = Services.GetExecutionQueueSettings();
            ExecutorListener e = null;

            var outputLogger = new WebExecutionLogger(LogRole);
                                    
            while (true)
            {
                bool reset = false;
                if (CheckForReset())
                {
                    reset = true;
                }

                if (reset)
                {
                    if (e != null)
                    {
                        e.Dispose();
                        e = null;
                    }
                }
                if (e == null)
                {
                    e = new ExecutorListener(local.RootPath, settings);
                    e.Stats.LastCacheReset = _lastResetTime;
                    e.Stats.Uptime = _startTime;
                }

                // Polling will invoke logger to write heartbeat status

                try
                {
                    bool didWork = e.Poll(outputLogger);
                    if (!didWork)
                    {
                        Thread.Sleep(2 * 1000);
                    }
                }
                catch (Exception ex)
                {
                    e.Stats.CriticalErrors++;
                    outputLogger.LogFatalError("Failure from Executor", ex);
                    outputLogger.WriteHeartbeat(e.Stats);
                }
              
            }            
        }

        private bool CheckForReset()
        {
            DateTime? last = Utility.GetBlobModifiedUtcTime(Secrets.GetExecutorResetControlBlob());
            if (!last.HasValue)
            {
                // if no control blob, then don't reset. Else we could be reseting on every poll.
                return false; 
            }
            if (last.Value > _lastResetTime)
            {
                _lastResetTime = last.Value;
                return true;
            }
            return false;
        }

        private static void LogRole(TextWriter output)
        {
            output.WriteLine("Role information:");
            output.WriteLine("  deployment id:{0}", RoleEnvironment.DeploymentId);
            output.WriteLine("  role {0} of {1}", RoleEnvironment.CurrentRoleInstance.Id, RoleEnvironment.Roles.Count);
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

    // Log execution instances to blob storage.
    public class WebExecutionLogger : IExecutionLogger
    {
        // Logging function for adding header info to the start of each log.
        Action<TextWriter> _addHeaderInfo;

        public WebExecutionLogger(Action<TextWriter> addHeaderInfo)
        {
            _addHeaderInfo = addHeaderInfo;
        }

        public FunctionOutputLog GetLogStream(FunctionInstance f)
        {
            CloudBlobContainer c = Secrets.GetExecutionLogContainer();
            string name = f.ToString() + ".txt";
            CloudBlob blob = c.GetBlobReference(name);
            
            var period = TimeSpan.FromMinutes(1); // frequency to refresh
            var x = new BlobIncrementalTextWriter(blob, period);

            TextWriter tw = x.Writer;
                        
            _addHeaderInfo(tw);

            return new FunctionOutputLog
            {
                CloseOutput = () =>
                {
                    x.Close();
                },
                Uri = blob.Uri.ToString(),
                Output = tw,
                ParameterLogBlob = new CloudBlobDescriptor
                {
                     AccountConnectionString = Secrets.AccountConnectionString,
                     ContainerName = Secrets.ExecutionLogName,
                     BlobName = f.ToString() + ".params.txt"
                }
            };
        }


        public void LogFatalError(string info, Exception e)
        {
            Services.LogFatalError(info, e);
        }    

        FunctionInvokeLogger invokeLogger = Services.GetFunctionInvokeLogger();

        public void UpdateInstanceLog(ExecutionInstanceLogEntity instance)
        {
            invokeLogger.Log(instance);

            // If complete, then queue a message to the orchestrator so it can aggregate stats. 

            if (instance.IsCompleted())
            {
                var queue = Secrets.GetExecutionCompleteQueue();
                var json = JsonCustom.SerializeObject(new ExecutionFinishedPayload { Instances = new Guid[] { instance.FunctionInstance.Id } });
                CloudQueueMessage msg = new CloudQueueMessage(json);
                queue.AddMessage(msg);
            }
        }

        public void WriteHeartbeat(ExecutionRoleHeartbeat stats)
        {
            Services.WriteHealthStatus(RoleEnvironment.CurrentRoleInstance.Id, stats);
        }


        public bool IsDeleteRequested(Guid id)
        {
            return Services.IsDeleteRequested(id);
        }
    }
}
