using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{   
    // Request information to invoke a function. 
    // This is just request information and doesn't contain any response information
    // This can be serialized. 
    // This has private information (account keys via Args) 
    public class FunctionInvokeRequest
    {
        // Versioning, to help detect against stale queue entries
        public int SchemaNumber { get; set; }

        // 3: Switched arguments to use polymorphism
        // 4: uses structured types in declarations.
        public const int CurrentSchema = 7;

        // Guid provides unique id to recognize function invocation instance.
        // This should get set once the function is queued. 
        public Guid Id { get; set; }

        // Diagnostic information about why this function was executed. 
        // Call ToString() to get a human readable reason. 
        // Assert: this.TriggerReason.ChildGuid == this.Id
        public TriggerReason TriggerReason { get; set; }

        public FunctionLocation Location { get; set; }

        public ParameterRuntimeBinding[] Args { get; set; }

        // $$$ Merge with other logging info, and with ExecutionInstanceLogEntity 
        // Output logging info
        // Blob to write live parameter logging too. 
        // Resolveed against default storage account
        public CloudBlobDescriptor ParameterLogBlob { get; set; }

        // This is a valid azure table row/partition key. 
        public override string ToString()
        {
            return Location.GetId() + "," + Id.ToString();
        }

        // ServiceURL. This can be used if the function needs to queue other execution requests.
        public string ServiceUrl { get; set; }
        
        // List of prerequisites. Null if no prereqs. 
        public Guid[] Prereqs { get; set; }

        // Do a clone of this object, but update the location.
        // This is useful as we convert between different location types (eg, after downloading)
        public FunctionInvokeRequest CloneUpdateLocation(FunctionLocation newLocation)
        {
            // Easiest to do a deep copy; but we could do a shallow since we're just changing the location.
            string json = JsonCustom.SerializeObject(this);
            var copy = JsonCustom.DeserializeObject<FunctionInvokeRequest>(json);

            copy.Location = newLocation;
            return copy;
        }
    }
}