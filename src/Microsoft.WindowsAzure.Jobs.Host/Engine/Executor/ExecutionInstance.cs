using System;
using System.IO;
using System.Threading;

namespace Microsoft.WindowsAzure.Jobs
{
    // Represents a single execution request. 
    internal class ExecutionInstance
    {
        // Local directory where execution has been copied to.
        private readonly string _localCopy;

        // Capture output and logging. 
        private TextWriter _output;

        // localCopy - local directory where execution has been copied to.
        // Facilitates multiple instances sharing the same execution path.
        internal ExecutionInstance(string localCopy, TextWriter outputLogging)
        {
            _output = outputLogging;
            _localCopy = localCopy;
        }

        internal FunctionExecutionResult Execute(FunctionInvokeRequest instance, CancellationToken token)
        {
            Console.WriteLine("# Executing: {0}", instance.Location.GetId());

            // Log
            _output.WriteLine("Executing: {0}", instance.Location.GetId());
            foreach (var arg in instance.Args)
            {
                _output.WriteLine("  Arg:{0}", arg.ToString());
            }
            _output.WriteLine();

            var localInstance = ConvertToLocal(instance);

            var result = ProcessHelper.ProcessExecute<FunctionInvokeRequest, FunctionExecutionResult>(
                typeof(RunnerProgram),
                _localCopy,
                localInstance, _output,
                token);

            return result;
        }

        private FunctionInvokeRequest ConvertToLocal(FunctionInvokeRequest remoteFunc)
        {
            var remoteLoc = (RemoteFunctionLocation)remoteFunc.Location;

            var localLocation = remoteLoc.GetAsLocal(_localCopy);

            var localFunc = remoteFunc.CloneUpdateLocation(localLocation);

            return localFunc;
        }
    }
}
