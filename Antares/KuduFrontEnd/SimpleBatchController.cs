using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Http;
using DaasEndpoints;
using Executor;
using Orchestrator;
using RunnerInterfaces;

namespace KuduFrontEnd
{
    // $$$ Also include a "HelpPage", similar to dashboard's invoke pages?
    // provides an HTML springboard of the functions in this deployment?

    public class SimpleBatchIndexerController : ApiController    
    {
        // Index through user functions 
        public FunctionDefinition[] Get()
        {
            string url = this.Request.RequestUri.ToString();

            // Azure storage account that all bindings get resolved relative to. 
            // !!!
            string accountConnectionString = "";

            // Can fail if accountConnectionString is not set.

            var ft = new IndexInMemory();
            Indexer i = new Indexer(ft);

            foreach (Assembly a in GetUserAssemblies())
            {
                i.IndexAssembly(m => OnApplyLocationInfo(url, accountConnectionString, m), a);
            }

            return ft.ReadAll();
        }

        IEnumerable<Assembly> GetUserAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies();
        }

        FunctionLocation OnApplyLocationInfo(string uri, string accountConnectionString,  MethodInfo method)
        {
            throw new NotImplementedException();
        }
    }
    

    // Invoke a simple batch function 
    public class SimpleBatchInvokeController : ApiController
    {
        public void Post(FunctionInvokeRequest request)
        {
            // !!! Move to background thread. 
            var loc = request.Location;
            // Invoke loc in-memory 

            // !!! Danger, we're passing the Service AccountInfo into the user's WebApi?
            // - needed for execution to update logs, callback into bridge when done. 
            IAccountInfo accountInfo = null;
            var services = new Services(accountInfo);

            string roleName = "kudu:" + Process.GetCurrentProcess().Id.ToString();
            var logger = new WebExecutionLogger(services, LogRole, roleName);

            Utility.DebugRunInProc = true;

            // IFunctionUpdatedLogger, ExecutionStatsAggregatorBridge, IFunctionOuputLogDispenser
            var ctx = logger.GetExecutionContext();
                        
            /* !!!
                ExecutionBase.Work(
                    request,
                    ctx,
                    fpInvokeFunc);
             */
        }


        private static void LogRole(TextWriter output)
        {
            output.WriteLine("Antares: pid:{0}", Process.GetCurrentProcess().Id);
        }
    }
}
