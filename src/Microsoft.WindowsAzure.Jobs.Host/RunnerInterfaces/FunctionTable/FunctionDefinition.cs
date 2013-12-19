using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Basic static definition for a function within SimpleBatch.
    // This has extra metadata beyond just FunctionLocation (like Descr, Guid (rowkey), in/out, ... )
    // This should be static once a function is uploaded. It can obviously change when we refresh a function and load
    // a new version. So the Timestamp property gives us "last modified time"
    // But it can't change just be executing the function. Store that invocation information somewhere else.
    internal class FunctionDefinition
    {
        // User description of the function
        public string Description { get; set; }

        // This maps to the builtin property on azure Tables, so it will get set for us.
        public DateTime Timestamp { get; set; }

        // Where the function lives. Location is effectively the row key.
        public FunctionLocation Location { get; set; }

        // What causes the function to get triggered.
        // This is used by the orchestrator service.
        public FunctionTrigger Trigger { get; set; }

        // How to bind the parameters.
        public FunctionFlow Flow { get; set; }

        public CloudStorageAccount GetAccount()
        {
            return Utility.GetAccount(this.Location.AccountConnectionString);
        }

        // This can be used as an azure row/partition key.
        public override string ToString()
        {
            return this.Location.ToString();
        }

        // Do a clone of this object, but update the location.
        // This is useful as we convert between different location types (eg, after downloading)
        public FunctionDefinition CloneUpdateLocation(FunctionLocation newLocation)
        {
            // Easiest to do a deep copy; but we could do a shallow since we're just changing the location.
            string json = JsonCustom.SerializeObject(this);
            var copy = JsonCustom.DeserializeObject<FunctionDefinition>(json);

            copy.Location = newLocation;
            return copy;
        }
    }
}
