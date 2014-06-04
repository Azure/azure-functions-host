using System;
using System.Runtime.ExceptionServices;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class FunctionInvocationResult
    {
        public Guid Id { get; set; }

        public bool Succeeded { get; set; }

        public IDelayedException ExceptionInfo { get; set; }
    }
}
