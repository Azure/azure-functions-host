using System;
using Microsoft.Azure.Jobs.Host.Runners;

namespace Microsoft.Azure.Jobs
{
    // Request information to invoke a function. 
    internal class FunctionInvokeRequest
    {
        // Guid provides unique id to recognize function invocation instance.
        public Guid Id { get; set; }

        // Diagnostic information about why this function was executed. 
        // Call ToString() to get a human readable reason. 
        // Assert: this.TriggerReason.ChildGuid == this.Id
        public TriggerReason TriggerReason { get; set; }

        public FunctionLocation Location { get; set; }

        public IParametersProvider ParametersProvider { get; set; }
    }
}
