using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;


namespace Microsoft.WindowsAzure.Jobs
{
    // This is the basic infomration that a static binding can use to create a runtime binding.
    // There are auxillary interfaces (ITrigger*) which provide additional information specific to certain binding triggers.
    internal interface IRuntimeBindingInputs
    {
        IDictionary<string, string> NameParameters { get; } 
        string AccountConnectionString { get; }
        string ReadFile(string filename); // $$$ Throw or null on missing file?
    }

    // extension interface to IRuntimeBindingInputs, for when the input is triggered by a new blob
    internal interface ITriggerNewBlob : IRuntimeBindingInputs
    {
        // If null, then ignore.
        CloudBlob BlobInput { get; }
    }

    // extension interface for when the input is triggered bya new queue message.
    internal interface ITriggerNewQueueMessage : IRuntimeBindingInputs
    {
        // If null, then ignore. 
        CloudQueueMessage QueueMessageInput { get; }
    }



    // $$$ Simulate via extension methods?
    // Replaces FunctionFlow. 
    // Indexer produces derived instances of this. . 
    // This must be serializable to JSON. Derived properties get serialized. 
    // [JsonIgnore] on a virtual property will be respected in derived classes. 
    internal abstract class ParameterStaticBinding
    {
        // Parameter's name. 
        // This is useful for diagnostic purposes. 
        public string Name { get; set; }

        // Validate statically. Throw InvalidOperation Exceptions if something is wrong in the bindings. 
        public virtual void Validate(IConfiguration config, ParameterInfo parameter)
        {
            // Nop
        }

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
        public abstract ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs);

        // This should roundtrip with ConvertToInvokeString
        public abstract ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString);

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
        public virtual TriggerDirectionType GetTriggerDirectionType()
        {
            return TriggerDirectionType.Ignore;
        }
    }

    internal enum TriggerDirectionType
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
    internal abstract class ParameterRuntimeBinding
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
        public abstract BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter);

        public override string ToString()
        {
            return this.ConvertToInvokeString();
        }
    }
}