using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal interface ITriggeredFunctionBinding<TTriggerValue> : IFunctionBinding
    {
        IReadOnlyDictionary<string, IValueProvider> Bind(RuntimeBindingProviderContext context, Guid functionInstanceId,
            TTriggerValue value);
    }
}
