using System;
using Microsoft.Azure.Jobs.Host;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    internal class WebExecutionLogger : IExecutionLogger
    {
        private readonly FunctionExecutionContext _ctx;

        public WebExecutionLogger(Guid hostInstanceId, string hostDisplayName, string sharedQueueName, CloudStorageAccount account)
        {
            _ctx = new FunctionExecutionContext
            {
                HostInstanceId = hostInstanceId,
                HostDisplayName = hostDisplayName,
                SharedQueueName = sharedQueueName,
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
