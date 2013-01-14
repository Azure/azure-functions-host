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


        public LocalFunctionInstance GetLocalFunctionInstance(string localDir)
        {
            string assemblyEntryPoint = Path.Combine(localDir, this.Location.Blob.BlobName);

            LocalFunctionInstance x = new LocalFunctionInstance
            {
                AssemblyPath = assemblyEntryPoint,
                TypeName = this.Location.TypeName,
                MethodName = this.Location.MethodName,
                Args = this.Args,
                ParameterLogBlob = this.ParameterLogBlob,
                Location = this.Location,
                ServiceUrl = this.ServiceUrl
            };
            return x;
        }
    }
}