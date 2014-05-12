using System;
using System.IO;
using System.Threading;

namespace Microsoft.Azure.Jobs.Internals
{
    // In-memory executor. 
    class WebSitesExecuteFunction : ExecuteFunctionBase
    {
        // Logging hook for each function invoked. 
        private readonly IJobHostLogger _fpLog;

        private readonly FunctionExecutionContext _ctx;

        private readonly IConfiguration _config;

        public WebSitesExecuteFunction(IConfiguration config, FunctionExecutionContext ctx, IJobHostLogger hostLogger = null)
        {
            _config = config;
            _fpLog = hostLogger;
            _ctx = ctx;
        }
        protected override ExecutionInstanceLogEntity Work(FunctionInvokeRequest request, CancellationToken cancellationToken)
        {
            var loc = request.Location;

            Func<TextWriter, CloudBlobDescriptor, FunctionExecutionResult> fpInvokeFunc =
                (consoleOutput, parameterLog) =>
                {
                    if (_fpLog != null)
                    {
                        _fpLog.LogFunctionStart(request);
                    }

                    // @@@ May need to be in a new appdomain. 
                    var oldOutput = Console.Out;
                    Console.SetOut(consoleOutput);

                    // @@@ May need to override config to set ICall
                    var result = RunnerProgram.MainWorker(parameterLog, request, _config, cancellationToken);
                    Console.SetOut(oldOutput);

                    return result;
                };

            // @@@ somewhere this should be async, handle long-running functions. 
            ExecutionInstanceLogEntity logItem = ExecutionBase.Work(
                request,
                _ctx,
                fpInvokeFunc);

            if (_fpLog != null)
            {
                _fpLog.LogFunctionEnd(logItem);
            }

            return logItem;
        }
    }
}
