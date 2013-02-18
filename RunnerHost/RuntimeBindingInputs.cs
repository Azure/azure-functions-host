using System.Collections.Generic;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace RunnerHost
{
    // Inputs that can impact producing a runtime binding.     
    public class RuntimeBindingInputs : IRuntimeBindingInputs
    {
        private FunctionLocation _location;

        // !!! this ctor means that ReadFile doesn't work. 
        // This gets invoked when a function uses IBinder to create stuff. 
        public RuntimeBindingInputs(string accountConnectionString)
        {
            this.AccountConnectionString = accountConnectionString;
        }

        public RuntimeBindingInputs(FunctionLocation location)
        {
            this._location = location;
            this.AccountConnectionString = location.Blob.AccountConnectionString;
        }

        // Account that binding is relative too. 
        // public CloudStorageAccount _account;
        public string AccountConnectionString { get; private set; }

        public IDictionary<string, string> NameParameters { get; set; }

        // Reads a file, relative to the function being executed. 
        public virtual string ReadFile(string filename)
        {
            var container = _location.Blob.GetContainer();
            var blob = container.GetBlobReference(filename);
            string content = Utility.ReadBlob(blob);

            return content;
        }
    }

    public class NewBlobRuntimeBindingInputs : RuntimeBindingInputs, ITriggerNewBlob
    {
        public NewBlobRuntimeBindingInputs(FunctionLocation location, CloudBlob blobInput)
            : base(location)
        {
            this.BlobInput = blobInput;
        }

        // The blob that triggered this input
        public CloudBlob BlobInput { get; private set; }
    }

    public class NewQueueMessageRuntimeBindingInputs : RuntimeBindingInputs, ITriggerNewQueueMessage
    {
        public NewQueueMessageRuntimeBindingInputs(FunctionLocation location, CloudQueueMessage queueMessage)
            : base(location)
        {
            this.QueueMessageInput = queueMessage;
        }

        // Non-null if this was triggered by a new azure Q message. 
        public CloudQueueMessage QueueMessageInput { get; private set; }        
    }
}