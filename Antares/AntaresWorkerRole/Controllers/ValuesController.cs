using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Web.Http;
using DaasEndpoints;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace AntaresWorkerRole.Controllers
{
    public class WorkerController : ApiController
    {
        // POST api/values
        public int Post([FromBody]AccountInfo value)
        {
            // Work can be long running. Return the HTTP request immediately. 
            Thread t = new Thread( () => Work(value));
            t.Start();

            return 1000;
        }

        void Work(IAccountInfo accountInfo)
        {
            // Antares permissions: a spawned process can't make network calls. 
            // So we must run in our process.
            Utility.DebugRunInProc = true;

            var services = new Services(accountInfo);
            CloudQueue executionQueue = services.GetExecutionQueue();

            string rootPath = Path.Combine(Path.GetTempPath(), "SimpleBatchRoot");

            var e = new ExecutorListener(rootPath, executionQueue);
            var outputLogger = new WebExecutionLogger(services, LogRole, "Antares:" + Process.GetCurrentProcess().Id.ToString());

            // Checks for message.
            while (true)
            {
                bool didWork = e.Poll(outputLogger);
                if (!didWork)
                {
                    break;
                }
            }

        }

        private static void LogRole(TextWriter output)
        {
            output.WriteLine("Antares: pid:{0}", Process.GetCurrentProcess().Id);
        }
        
    }
}