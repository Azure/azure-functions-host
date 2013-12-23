using System;
using System.IO;

namespace Microsoft.WindowsAzure.Jobs.Internals
{
    // In-memory executor. 
    class AntaresQueueFunction : QueueFunctionBase
    {
        // Logging hook for each function invoked. 
        private readonly IJobHostLogger _fpLog;

        private readonly FunctionExecutionContext _ctx;

        private readonly IConfiguration _config;

        public AntaresQueueFunction(QueueInterfaces interfaces, IConfiguration config, FunctionExecutionContext ctx, IJobHostLogger hostLogger = null)
            : base(interfaces)
        {
            _config = config;
            _fpLog = hostLogger;
            _ctx = ctx;
        }
        protected override void Work(ExecutionInstanceLogEntity logItem)
        {
            var request = logItem.FunctionInstance;
            var loc = request.Location;

            Func<TextWriter, FunctionExecutionResult> fpInvokeFunc =
                (consoleOutput) =>
                {
                    if (_fpLog != null)
                    {
                        _fpLog.LogFunctionStart(request);
                    }

                    // @@@ May need to be in a new appdomain. 
                    var oldOutput = Console.Out;
                    Console.SetOut(consoleOutput);

                    // @@@ May need to override config to set ICall
                    var result = RunnerProgram.MainWorker(request, _config);
                    Console.SetOut(oldOutput);

                    return result;
                };

            // @@@ somewhere this should be async, handle long-running functions. 
            ExecutionBase.Work(
                request,
                _ctx,
                fpInvokeFunc);

            if (_fpLog != null)
            {
                var logFinal = _lookup.Lookup(request);
                _fpLog.LogFunctionEnd(logFinal);
            }
        }
    }
}
