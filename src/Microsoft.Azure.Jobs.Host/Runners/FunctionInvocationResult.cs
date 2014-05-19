using System;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class FunctionInvocationResult
    {
        public Guid Id { get; set; }

        public bool Succeeded { get; set; }

        public string ExceptionMessage { get; set; }
    }
}
