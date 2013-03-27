using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using DaasEndpoints;
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
            ServiceContainer container = new ServiceContainer();
            container.SetService<IAccountInfo>(new AzureRoleAccountInfo());

            Run(container);
        }

        void Run(IServiceContainer serviceContainer)
        {
            _startTime = DateTime.UtcNow;

            // This is a sample worker implementation. Replace with your logic.
            Trace.WriteLine("WorkerRole1 entry point called", "Information");

            var local = RoleEnvironment.GetLocalResource("localStore");

            IAccountInfo accountInfo = serviceContainer.GetService<IAccountInfo>();
            
            Recorder.Start(accountInfo.GetAccountName());
            

            var services = new Services(accountInfo);
            CloudQueue executionQueue = services.GetExecutionQueue();
            ExecutorListener e = null;

            var outputLogger = new WebExecutionLogger(services, LogRole);

            while (true)
            {
                bool reset = false;
                if (CheckForReset(services))
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
                    e = new ExecutorListener(local.RootPath, executionQueue);
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

        private bool CheckForReset(Services services)
        {
            var blob = services.GetExecutorResetControlBlob();
            DateTime? last = Utility.GetBlobModifiedUtcTime(blob);
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


    // Provide services for executing a function on a Worker Role.
    // FunctionExecutionContext is the common execution operations that aren't Worker-role specific.
    // Everything else is worker role specific. 
    public class WebExecutionLogger : IExecutionLogger
    {
        // Logging function for adding header info to the start of each log.
        private readonly Services _services;
        private readonly FunctionExecutionContext _ctx;

        public WebExecutionLogger(Services services, Action<TextWriter> addHeaderInfo)
        {
            _services = services;

            _ctx = new FunctionExecutionContext
            {
                OutputLogDispenser = new FunctionOutputLogDispenser( 
                    _services.AccountInfo, 
                    addHeaderInfo, 
                    AzureExecutionEndpointNames.ConsoleOuputLogContainerName
                ),
                Bridge = _services.GetStatsAggregatorBridge(),
                Logger = _services.GetFunctionInvokeLogger()
            };
        }
                 
        public FunctionExecutionContext GetExecutionContext()
        {
            return _ctx;
        }
        
        public void LogFatalError(string info, Exception e)
        {
            _services.LogFatalError(info, e);
        }    
                
        public void WriteHeartbeat(ExecutionRoleHeartbeat stats)
        {
            _services.WriteHealthStatus(RoleEnvironment.CurrentRoleInstance.Id, stats);
        }

        public bool IsDeleteRequested(Guid id)
        {
            return _services.IsDeleteRequested(id);
        }
    }


}
