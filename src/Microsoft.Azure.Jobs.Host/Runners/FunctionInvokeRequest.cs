using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;

namespace Microsoft.Azure.Jobs
{
    // Request information to invoke a function. 
    internal class FunctionInvokeRequest
    {
        // Guid provides unique id to recognize function invocation instance.
        public Guid Id { get; set; }

        public MethodInfo Method { get; set; }

        public IParametersProvider ParametersProvider { get; set; }

        public ExecutionReason Reason { get; set; }

        public Guid? ParentId { get; set; }
    }
}
