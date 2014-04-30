using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs
{
    // Provide services for executing a function on a Worker Role.
    // FunctionExecutionContext is the common execution operations that aren't Worker-role specific.
    // Everything else is worker role specific. 
    internal class WebExecutionLogger : IExecutionLogger
    {
        private readonly FunctionExecutionContext _ctx;

        public WebExecutionLogger(Guid hostInstanceId, Services services, Action<TextWriter> addHeaderInfo)
        {
            _ctx = new FunctionExecutionContext
            {
                HostInstanceId = hostInstanceId,
                OutputLogDispenser = new FunctionOutputLogDispenser(
                    services.AccountInfo,
                    addHeaderInfo,
                    ContainerNames.ConsoleOuputLogContainerName
                ),
                Logger = services.GetFunctionUpdatedLogger()
            };
        }

        public FunctionExecutionContext GetExecutionContext()
        {
            return _ctx;
        }
    }
}
