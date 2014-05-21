using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal interface ITriggerData
    {
        IValueProvider ValueProvider { get; }

        IReadOnlyDictionary<string, object> BindingData { get; }
    }
}
