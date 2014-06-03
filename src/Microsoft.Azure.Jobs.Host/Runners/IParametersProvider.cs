using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal interface IParametersProvider
    {
        IReadOnlyDictionary<string, IBinding> NonTriggerBindings { get; }

        IReadOnlyDictionary<string, IValueProvider> Bind();
    }
}
