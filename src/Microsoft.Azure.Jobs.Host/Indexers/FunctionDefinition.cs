using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;

namespace Microsoft.Azure.Jobs
{
    internal class FunctionDefinition
    {
        // Where the function lives.
        public FunctionLocation Location { get; set; }

        // How to bind the parameters. Will eventually be encapsulated behind Executor & Listener properties.
        public string TriggerParameterName { get; set; }
        public ITriggerBinding TriggerBinding { get; set; }
        public IReadOnlyDictionary<string, IBinding> NonTriggerBindings { get; set; }

        public FunctionDescriptor ToFunctionDescriptor()
        {
            IDictionary<string, ParameterDescriptor> parameters = new Dictionary<string, ParameterDescriptor>();

            if (TriggerBinding != null)
            {
                parameters.Add(TriggerParameterName, TriggerBinding.ToParameterDescriptor());
            }

            foreach (KeyValuePair<string, IBinding> item in NonTriggerBindings)
            {
                parameters.Add(item.Key, item.Value.ToParameterDescriptor());
            }

            return new FunctionDescriptor
            {
                Id = Location.GetId(),
                FullName = Location.FullName,
                ShortName = Location.GetShortName(),
                Parameters = parameters
            };
        }
    }
}
