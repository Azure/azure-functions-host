using System;
using Microsoft.Azure.Jobs.Host.Loggers;

namespace Microsoft.Azure.Jobs
{
    // Various objects needed for execution.
    // @@@ Confirm this can be shared across requests
    internal class FunctionExecutionContext
    {
        public Guid HostId { get; set; }

        public Guid HostInstanceId { get; set; }

        public IFunctionOuputLogDispenser OutputLogDispenser { get; set; }

        public IFunctionInstanceLogger FunctionInstanceLogger { get; set; }
    }
}
