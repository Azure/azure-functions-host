using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{
    // Stuff that's shared between RunnerHost and Executor?

    // Describes a function that resides on this machine (so we can use a path to the local harddrive).
    // Runner takes in an InvokeDescription, which is serialized from the executor.
    public class LocalFunctionInstance
    {
        public string AssemblyPath { get; set; }
        public string TypeName { get; set; }
        public string MethodName { get; set; }

        public ParameterRuntimeBinding[] Args { get; set; }

        // Blob to write live parameter logging too. 
        public CloudBlobDescriptor ParameterLogBlob { get; set; }

        // ServiceURL. This can be used if the function needs to queue other execution requests.
        public string ServiceUrl { get; set; }

        // Location of this function in the cloud. This is useful to:
        // - provide a storage container that everything gets resolved against.
        // - provide a scope for resolving other execution requests (account/container/blob/assembly/type/method).
        public FunctionLocation Location { get; set; }
    }

    // Results from function execution, produced by runner host. 
    public class FunctionExecutionResult
    {
        // null on success. Type.FullName if the function threw an exception.
        public string ExceptionType { get; set; }
        public string ExceptionMessage { get; set; }
    }
        
    // Full permission to a queue
    public class CloudQueueDescriptor
    {
        public string AccountConnectionString { get; set; }
        public string QueueName { get; set; }

        public CloudStorageAccount GetAccount()
        {
            return Utility.GetAccount(AccountConnectionString);
        }

        public CloudQueue GetQueue()
        {
            var q = GetAccount().CreateCloudQueueClient().GetQueueReference(QueueName);
            q.CreateIfNotExist();
            return q;
        }        

        public string GetId()
        {
            string accountName = GetAccount().Credentials.AccountName;
            return string.Format(@"{0}\{1}", accountName, QueueName);
        }

        public override string ToString()
        {
            return GetId();
        }
    } 

    // Queue message payload to request that orchestrator rescan a blob path 
    public class IndexRequestPayload
    {
        public string AccountConnectionString { get; set; }

        public string Blobpath { get; set; }

        // URI to upload to with index results. 
        // Use string instead of URI type to avoid escaping. That confuses azure.
        public string Writeback { get; set; }
    }

    // Full permission to a Table
    // This can be serialized. 
    public class CloudTableDescriptor
    {
        public string AccountConnectionString { get; set; }

        public string TableName { get; set; }

        public CloudStorageAccount GetAccount()
        {
            return Utility.GetAccount(AccountConnectionString);
        }
    }

    // Full permission to a blob
    // $$$ This class morphed a litte. Is this now the same as CloudBlobContainer?
    // - vs. CloudBlob: this blobName can be null, this can refer to open. things. 
    // - vs. CloudBlobPath: this has account info. 
    public class CloudBlobDescriptor
    {
        public string AccountConnectionString { get; set; }
        
        public string ContainerName { get; set; }
        public string BlobName { get; set; }
        
        public CloudStorageAccount GetAccount()
        {
            return Utility.GetAccount(AccountConnectionString);
        }

        public CloudBlobContainer GetContainer()
        {
            var client = GetAccount().CreateCloudBlobClient();
            var c = Utility.GetContainer(client, ContainerName);
            return c;
        }

        public CloudBlob GetBlob()
        {
            var c = GetContainer();
            c.CreateIfNotExist();
            var blob = c.GetBlobReference(BlobName);
            return blob;
        }

        public string GetId()
        {
            string accountName = GetAccount().Credentials.AccountName;
            return string.Format(@"{0}\{1}\{2}", accountName, ContainerName, BlobName);
        }
                
        public override string ToString()
        {
            return GetId();
        }
    }

    // Function Descriptor vs. Instance    
    // This can be serialized. 
    // This has private information (account keys via Args)
    // ### Rename this to "request".
    public class FunctionInstance
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
