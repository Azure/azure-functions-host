using System;
using System.IO;
using Microsoft.Azure.Jobs.Host;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    // Provide services for executing a function on a Worker Role.
    // FunctionExecutionContext is the common execution operations that aren't Worker-role specific.
    // Everything else is worker role specific. 
    internal class WebExecutionLogger : IExecutionLogger
    {
        private readonly FunctionExecutionContext _ctx;

        public WebExecutionLogger(Guid hostId, Guid hostInstanceId, CloudStorageAccount account, Action<TextWriter> addHeaderInfo)
        {
            _ctx = new FunctionExecutionContext
            {
                HostId = hostId,
                HostInstanceId = hostInstanceId,
                OutputLogDispenser = new FunctionOutputLogDispenser(
                    account,
                    addHeaderInfo,
                    HostContainerNames.ConsoleOuputLogContainerName
                )
            };
        }

        public FunctionExecutionContext GetExecutionContext()
        {
            return _ctx;
        }
    }
}
