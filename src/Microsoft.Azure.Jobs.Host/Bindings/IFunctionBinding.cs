using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IFunctionBinding
    {
        IReadOnlyDictionary<string, IValueProvider> Bind(RuntimeBindingProviderContext context, Guid functionInstanceId,
            IDictionary<string, object> parameters);
    }
}
