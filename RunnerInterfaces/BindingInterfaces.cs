using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using SimpleBatch;

namespace RunnerInterfaces
{    
    // Inputs that can impact producing a runtime binding.     
    public class RuntimeBindingInputs
    {
        private FunctionLocation _location;

        // !!! this ctor means that ReadFile doesn't work. 
        // This gets invoked when a function uses IBinder to create stuff. 
        public RuntimeBindingInputs(string accountConnectionString)
        {
            this.Account = Utility.GetAccount(accountConnectionString);
        }

        public RuntimeBindingInputs(FunctionLocation location)
        {
            this._location = location;
            this.Account = Utility.GetAccount(location.Blob.AccountConnectionString);
        }

        // Account that binding is relative too. 
        // public CloudStorageAccount _account;
        public CloudStorageAccount Account { get; set; }

        // $$$ Input triggered on CloudBlob, or triggered on a new Message
        public CloudBlob _blobInput;
        public CloudQueueMessage _queueMessageInput;

        public IDictionary<string, string> _nameParameters;

        // Reads a file, relative to the function being executed. 
        public virtual string ReadFile(string filename)
        {
            var container = _location.Blob.GetContainer();
            var blob = container.GetBlobReference(filename);
            string content= Utility.ReadBlob(blob);

            return content;
        }
    }

    // $$$ Simulate via extension methods?
    // Replaces FunctionFlow. 
    // Indexer produces derived instances of this. . 
    // This must be serializable to JSON. Derived properties get serialized. 
    // [JsonIgnore] on a virtual property will be respected in derived classes. 
    public abstract class ParameterStaticBinding
    {
        // Parameter's name. 
        // This is useful for diagnostic purposes. 
        public string Name { get; set; }

        // Get any {name} keys that this binding will provide. 
        // This notifies the static binder about which names are available for other bindings
        [JsonIgnore]
        public virtual IEnumerable<string> ProducedRouteParameters
        {
            get { return _empty; }
        }    

        private static string[] _empty = new string[0];

        // BindingContext provides set of possible inputs  (a single blob input that triggered, a queue message, 
        // a dictionary of name parameters)
        // This is what BindingContext.Bind does.
        public abstract ParameterRuntimeBinding Bind(RuntimeBindingInputs inputs);

        // This should roundtrip with ConvertToInvokeString
        public abstract ParameterRuntimeBinding BindFromInvokeString(CloudStorageAccount account, string invokeString);

        // Describe the binding, as understood by the indexer. 
        //   Read from blob "container\blob\{name}.csv"
        //   Write to table  'foo'
        //   Access route parameter {name}
        //
        // Could use GetInputString()  to tag on message: ", provides route parameters {name}
        [JsonIgnore]
        public abstract string Description { get; }

        public override string ToString()
        {
            return Description;        
        }

        // Function to avoid being serialized. WCF hangs on seing Enums in a serialization payload.  
        public virtual TriggerType GetTriggerType()
        {
            return TriggerType.Ignore;
        }
    }

    public enum TriggerType
    {
        // Parameter is an input that we can reason about and can trigger (eg,  [BlobInput])
        Input,

        // Parameter is an output that we can reason about  (eg [BlobOutput])
        Output,

        // Parameter does not cause triggering. 
        Ignore
    }

    // Represents a parameter instance, used to invoke an instance of a function.
    // This can be serialized and stored int he payload of a execution request. 
    // In the runner host process, it gets converted into a System.Object for finally invoking a function.    
    // This can do lots of heavy stuff in the binder, like converting a Blob to 18 different runtime types.     
    // This Serializes to JSON. 
    public abstract class ParameterRuntimeBinding
    {        
        // Get a "human readable" string that can be displayed and passed to BindFromString
        // This can be part of the UI. 
        public abstract string ConvertToInvokeString();
        
        // When was this parameter last modified? 
        // This applies to both inputs and outputs. (eg, think blobs).
        // Can be used in optimizations. 
        // Null if not avaiable. (eg, if we have no way to scan, such as a table input; blob output may not yet be written)
        [JsonIgnore]
        public virtual DateTime? LastModifiedTime
        {
            get { return null; } // no time information known 
        }

        // Get a runtime object. 
        // Include ParameterInfo because those don't serialize. 
        // Also, the parameterInfo provides a loader-approved System.Type for the target parameter, so
        // that avoids trying to serialize and rehydrate a type.
        // This should be the same parameter info that the function was originally indexed against. 
        public abstract BindResult Bind(IConfiguration config, IBinder bindingContext, ParameterInfo targetParameter);

        public override string ToString()
        {
            return this.ConvertToInvokeString();
        }
    }
}