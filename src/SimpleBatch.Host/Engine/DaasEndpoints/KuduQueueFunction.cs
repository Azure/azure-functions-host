using System;
using System.Diagnostics;
using System.IO;

using Microsoft.WindowsAzure.StorageClient;


namespace Microsoft.WindowsAzure.Jobs
{
    // Run a function where the source (and binaries) are via Kudu
    class KuduQueueFunction : QueueFunctionBase
    {
        public KuduQueueFunction(QueueInterfaces interfaces)
            : base(interfaces)
        {
        }
        protected override void Work(ExecutionInstanceLogEntity logItem)
        {
            var request = logItem.FunctionInstance;
            var loc = request.Location;

            var x = loc as IUrlFunctionLocation;
            if (x != null)
            {
                // Invoke(x, request);
                Run(request);
            }

            // $$$ Ignore others. Could chain to another IQueueFunction impl. 
        }

        KuduFunctionExecutionResult Invoke(IUrlFunctionLocation x, FunctionInvokeRequest request)
        {
            // $$$ This is synchronous. Make it async. But that means plumbing through ExecutionBase.Work.
            return Utility.PostJson<KuduFunctionExecutionResult>(x.InvokeUrl, request);
        }

        void Run(FunctionInvokeRequest request)
        {
            IAccountInfo accountInfo = this._account;
            var services = new Services(accountInfo);

            string roleName = "kudu:" + Process.GetCurrentProcess().Id.ToString();
            var logger = new WebExecutionLogger(services, LogRole, roleName);

            // IFunctionUpdatedLogger, ExecutionStatsAggregatorBridge, IFunctionOuputLogDispenser
            var ctx = logger.GetExecutionContext();


            Func<TextWriter, FunctionExecutionResult> fpInvokeFunc =
                (consoleOutput) =>
                {
                    var loc = (IUrlFunctionLocation) request.Location;
                    var result = Invoke(loc, request);
                    consoleOutput.WriteLine(result.ConsoleOutput);

                    return result.Result;
                };

            ExecutionBase.Work(
                request,
                ctx,
                fpInvokeFunc);
             
        }

        private static void LogRole(TextWriter output)
        {
            output.WriteLine("Kudu {0}", Process.GetCurrentProcess().Id);
        }
    }
}