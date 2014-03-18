using System;
using Microsoft.WindowsAzure.Jobs.Host.Protocols;

namespace Microsoft.WindowsAzure.Jobs
{
    // Various objects needed for execution.
    // @@@ Confirm this can be shared across requests
    internal class FunctionExecutionContext
    {
        public Guid HostInstanceId { get; set; }

        public IFunctionOuputLogDispenser OutputLogDispenser { get; set; }

        // Used to update function as its being executed
        public IFunctionUpdatedLogger Logger { get; set; }

        public IFunctionInstanceLogger FunctionInstanceLogger { get; set; }

        // used for reporting WebJob to Function correlation
        public IFunctionsInJobIndexer FunctionsInJobIndexer{ get; set; }
    }
}
