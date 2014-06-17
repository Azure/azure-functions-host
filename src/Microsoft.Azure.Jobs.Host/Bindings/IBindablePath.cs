using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IBindablePath<TPath>
    {
        bool IsBound { get; }

        IEnumerable<string> ParameterNames { get; }

        TPath Bind(IReadOnlyDictionary<string, object> bindingData);

        string ToString();
    }
}
