using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{
    // Function Descriptor vs. Instance    
    // This can be serialized. 
    // This has private information (account keys via Args)
    // ### Rename this to "request".
    public class FunctionInvokeRequest
    {
        // Versioning, to help detect against stale queue entries
        public int SchemaNumber { get; set; }

        // 3: Switched arguments to use polymorphism
        // 4: uses structured types in declarations.
        public const int CurrentSchema = 5;

        // Guid provides unique id to recognize function invocation instance.
        // This should get set once the function is queued. 
        public Guid Id { get; set; }

        // Human readable string providing a hint for why this function was triggered.
        // This is for diagnostics. 
        public string TriggerReason { get; set; }

        public FunctionLocation Location { get; set; }

        public ParameterRuntimeBinding[] Args { get; set; }

        // $$$ Merge with other logging info, and with ExecutionInstanceLogEntity 
        // Output logging info
        // Blob to write live parameter logging too. 
        // Resolveed against default storage account
        public CloudBlobDescriptor ParameterLogBlob { get; set; }

        public override string ToString()
        {
            return Location.GetId() + "," + Id.ToString();
        }

        // ServiceURL. This can be used if the function needs to queue other execution requests.
        public string ServiceUrl { get; set; }
    }
}