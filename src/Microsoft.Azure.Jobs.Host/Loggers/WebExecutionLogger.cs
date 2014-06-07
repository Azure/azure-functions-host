using System;
using Microsoft.Azure.Jobs.Host;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    internal class WebExecutionLogger : IExecutionLogger
    {
        private readonly FunctionExecutionContext _ctx;

        public WebExecutionLogger(HostOutputMessage hostOutputMessage, CloudStorageAccount account)
        {
            _ctx = new FunctionExecutionContext
            {
                HostOutputMessage = hostOutputMessage,
                OutputLogDispenser = new FunctionOutputLogDispenser(
                    account,
                    HostContainerNames.ConsoleOutputLogContainerName
                )
            };
        }

        public FunctionExecutionContext GetExecutionContext()
        {
            return _ctx;
        }
    }
}
