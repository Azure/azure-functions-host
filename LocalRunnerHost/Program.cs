using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Executor;
using RunnerInterfaces;

namespace LocalRunnerHost
{
    // Test harness for lcoally replyaing a failed job. 
    class Program
    {
        static void Main(string[] args)
        {
            // Id is the FunctionInstanceId of the job. 
            // This will replay the existing instance, as opposed to creating a new instance.
            Guid id = new Guid(args[0]);

            var l = Services.GetFunctionInvokeLogger();
            var log = l.Get(id);
            FunctionInstance instance = log.FunctionInstance;

            //string temp = Path.Combine(Path.GetTempFileName(), "simplebatch");
            string temp = @"c:\temp\localtest";

            CancellationTokenSource source = new CancellationTokenSource();

            // for easier debugging. $$$ Have launched process call Debugger.Attach instead
            Utility.DebugRunInProc = true; 

            Executor.Executor e = new Executor.Executor(temp);
            var result = e.Execute(instance, Console.Out, source.Token);
        }
        
    }
}
