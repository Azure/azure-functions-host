using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IFunctionBinding
    {
        IReadOnlyDictionary<string, IValueProvider> Bind(FunctionBindingContext context,
            IDictionary<string, object> parameters);
    }
}
