using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Protocols;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs
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

        // BinderEx provides set of possible inputs  (a single blob input that triggered, a queue message, 
        // a dictionary of name parameters)
        // This is what BinderEx.Bind does.
        public abstract ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs);

        // This should roundtrip with ConvertToInvokeString
        public abstract ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString);

        public abstract ParameterDescriptor ToParameterDescriptor();
    }
}
