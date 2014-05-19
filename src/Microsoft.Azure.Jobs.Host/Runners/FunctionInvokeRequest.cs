using System;

namespace Microsoft.Azure.Jobs
{
    // Request information to invoke a function. 
    // This is just request information and doesn't contain any response information
    // This can be serialized. 
    // This has private information (account keys via Args) 
    internal class FunctionInvokeRequest
    {
        // Guid provides unique id to recognize function invocation instance.
        // This should get set once the function is queued. 
        public Guid Id { get; set; }

        // Diagnostic information about why this function was executed. 
        // Call ToString() to get a human readable reason. 
        // Assert: this.TriggerReason.ChildGuid == this.Id
        public TriggerReason TriggerReason { get; set; }

        public FunctionLocation Location { get; set; }

        public ParameterRuntimeBinding[] Args { get; set; }

        // This is a valid azure table row/partition key. 
        public override string ToString()
        {
            return Location.GetId() + "," + Id.ToString();
        }
    }
}
