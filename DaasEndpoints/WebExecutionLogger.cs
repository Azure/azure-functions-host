using System;
using System.IO;
using DaasEndpoints;
using RunnerInterfaces;
namespace Executor
{
    // Provide services for executing a function on a Worker Role.
    // FunctionExecutionContext is the common execution operations that aren't Worker-role specific.
    // Everything else is worker role specific. 
    public class WebExecutionLogger : IExecutionLogger
    {
        // Logging function for adding header info to the start of each log.
        private readonly Services _services;
        private readonly FunctionExecutionContext _ctx;
        private readonly string _roleName; // for logging heartbeat

        public WebExecutionLogger(Services services, Action<TextWriter> addHeaderInfo, string roleName)
        {
            _services = services;
            _roleName = roleName;

            _ctx = new FunctionExecutionContext
            {
                OutputLogDispenser = new FunctionOutputLogDispenser(
                    _services.AccountInfo,
                    addHeaderInfo,
                    AzureExecutionEndpointNames.ConsoleOuputLogContainerName
                ),
                Bridge = _services.GetStatsAggregatorBridge(),
                Logger = _services.GetFunctionUpdatedLogger()
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
            _services.WriteHealthStatus(_roleName, stats);
        }

        public bool IsDeleteRequested(Guid id)
        {
            return _services.IsDeleteRequested(id);
        }
    }
}