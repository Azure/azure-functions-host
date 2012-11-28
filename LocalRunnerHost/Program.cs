using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DaasEndpoints;
using Executor;
using RunnerInterfaces;

namespace LocalRunnerHost
{
    // Test harness for lcoally replyaing a failed job. 
    partial class Program
    {
        static void Main(string[] args)
        {
            // Id is the FunctionInstanceId of the job. 
            // This will replay the existing instance, as opposed to creating a new instance.
            Guid id = new Guid(args[0]);
            string configPath = args[1];

            IAccountInfo accountInfo = GetAccountInfo(configPath);
            FunctionInstance instance = GetFunctionInstance(accountInfo, id);

            //string temp = Path.Combine(Path.GetTempFileName(), "simplebatch");
            string tempPath = @"c:\temp\localtest";

            // for easier debugging, run everything in the same process. 
            // $$$ Have launched process call Debugger.Attach instead
            Utility.DebugRunInProc = true; 

            Executor.Executor e = new Executor.Executor(tempPath);
            var result = e.Execute(instance, Console.Out, CancellationToken.None);
        }

        static FunctionInstance GetFunctionInstance(IAccountInfo accountInfo, Guid id)
        {
            var services = new Services(accountInfo);

            FunctionInvokeLogger l = services.GetFunctionInvokeLogger();
            ExecutionInstanceLogEntity log = l.Get(id);
            if (log == null)
            {
                string name = accountInfo.GetAccountName();
                string msg = string.Format("Guid {0} is not a valid function id in the given storage account '{1}'", 
                    id, name);
                throw new InvalidOperationException(msg);
            }
            FunctionInstance instance = log.FunctionInstance;

            return instance;
        }
    }
}
