using System;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs
{
    internal class FunctionExecutionContext
    {
        public Guid HostInstanceId { get; set; }

        public string HostDisplayName { get; set; }

        public string SharedQueueName { get; set; }

        public HeartbeatDescriptor HeartbeatDescriptor { get; set; }

        public IFunctionOuputLogDispenser OutputLogDispenser { get; set; }

        public IFunctionInstanceLogger FunctionInstanceLogger { get; set; }
    }
}
