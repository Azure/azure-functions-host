using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;

namespace Microsoft.WindowsAzure.Jobs
{
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
        //   Read from blob "container/blob/{name}.csv"
        //   Write to table  'foo'
        //   Access route parameter {name}
        //
        // Could use GetInputString()  to tag on message: ", provides route parameters {name}
        [JsonIgnore]
        public abstract string Description { get; }

        // The default value to use when running the function from the dashboard.
        [JsonIgnore]
        public abstract string DefaultValue { get; }

        // When running the function from the dashboard, the text to display to indicate what kind of value to enter.
        [JsonIgnore]
        public abstract string Prompt { get; }

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
}
