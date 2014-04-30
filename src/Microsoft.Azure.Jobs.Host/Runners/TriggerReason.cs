using System;

namespace Microsoft.Azure.Jobs
{
    // Provides a structured description for why a function was executed. 
    // Call ToString() for human readable string. 
    // Cast to a derived class to get more specific structured information.
    // These get serialized via JSON and stored in tables. 
    internal abstract class TriggerReason
    {
        // The "current" guid that the rest of the trigger reason is valid for. 
        public Guid ChildGuid { get; set; }

        // Guid of parent function that triggered this one. 
        // Eg, if this func is triggered on [BlobInput], ParentGuid is the guid that wrote the blob. 
        // This is empty if there is no parent function (eg, an unknown blob writer).  
        public Guid ParentGuid { get; set; }

        // $$$ Also include Line number in parent function? Other diag info?

        public override string ToString()
        {
            return "Unknown reason";
        }
    }
}
