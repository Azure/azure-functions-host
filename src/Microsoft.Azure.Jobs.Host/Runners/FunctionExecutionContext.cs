using System;
using Microsoft.Azure.Jobs.Host.Loggers;

namespace Microsoft.Azure.Jobs
{
    internal class FunctionExecutionContext
    {
        public Guid HostInstanceId { get; set; }

        public string HostDisplayName { get; set; }

        public string SharedQueueName { get; set; }

        public IFunctionOuputLogDispenser OutputLogDispenser { get; set; }

        public IFunctionInstanceLogger FunctionInstanceLogger { get; set; }
    }
}
