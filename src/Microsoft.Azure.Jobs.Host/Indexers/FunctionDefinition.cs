using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    internal class FunctionDefinition
    {
        // Where the function lives. Location is effectively the row key.
        public FunctionLocation Location { get; set; }

        // How to bind the parameters (old style).
        public FunctionFlow Flow { get; set; }

        public CloudStorageAccount GetAccount()
        {
            return Utility.GetAccount(Location.StorageConnectionString);
        }

        // How to bind the parameters (new style). Will eventually be encapsulated behind Executor & Listener properties.
        public string TriggerParameterName { get; set; }
        public ITriggerBinding TriggerBinding { get; set; }
        public IReadOnlyDictionary<string, IBinding> NonTriggerBindings { get; set; }

        // This can be used as an azure row/partition key.
        public override string ToString()
        {
            return Location.ToString();
        }

        public FunctionDescriptor ToFunctionDescriptor()
        {
            IDictionary<string, ParameterDescriptor> parameters = new Dictionary<string, ParameterDescriptor>();

            if (TriggerBinding != null)
            {
                parameters.Add(TriggerParameterName, TriggerBinding.ToParameterDescriptor());
            }

            foreach (ParameterStaticBinding binding in Flow.Bindings)
            {
                if (parameters.ContainsKey(binding.Name))
                {
                    continue;
                }

                parameters.Add(binding.Name, binding.ToParameterDescriptor());
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
