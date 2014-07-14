using System;
using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class FunctionInvocationResult
    {
        public Guid Id { get; set; }

        public bool Succeeded { get; set; }

        public IDelayedException ExceptionInfo { get; set; }
    }
}
