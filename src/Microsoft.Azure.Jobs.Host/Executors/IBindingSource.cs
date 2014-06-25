using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal interface IBindingSource
    {
        IReadOnlyDictionary<string, IValueProvider> Bind(FunctionBindingContext context);
    }
}
