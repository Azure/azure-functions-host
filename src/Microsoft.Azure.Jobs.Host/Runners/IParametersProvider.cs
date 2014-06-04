using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Triggers;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal interface IParametersProvider
    {
        string TriggerParameterName { get; }

        ITriggerBinding TriggerBinding { get; }

        IReadOnlyDictionary<string, IBinding> NonTriggerBindings { get; }

        IReadOnlyDictionary<string, IValueProvider> Bind();
    }
}
