using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Triggers;

namespace Microsoft.Azure.Jobs
{
    // Request information to invoke a function. 
    internal class FunctionInvokeRequest
    {
        // Guid provides unique id to recognize function invocation instance.
        public Guid Id { get; set; }

        // Diagnostic information about why this function was executed. 
        // Call ToString() to get a human readable reason. 
        // Assert: this.TriggerReason.ChildGuid == this.Id
        public TriggerReason TriggerReason { get; set; }

        public FunctionLocation Location { get; set; }

        // TODO: Cleanly separate layers that see bindings from layers that see value providers.
        public string TriggerParameterName { get; set; }
        public ITriggerData TriggerData { get; set; }
        public IReadOnlyDictionary<string, IBinding> NonTriggerBindings { get; set; }

        public IReadOnlyDictionary<string, IValueProvider> Parameters { get; set; }

        // This is a valid azure table row/partition key. 
        public override string ToString()
        {
            return Location.GetId() + "," + Id.ToString();
        }
    }
}
