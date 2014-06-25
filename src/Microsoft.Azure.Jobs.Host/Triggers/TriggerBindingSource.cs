using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class TriggerBindingSource<TTriggerValue> : IBindingSource
    {
        private readonly ITriggeredFunctionBinding<TTriggerValue> _functionBinding;
        private readonly TTriggerValue _value;

        public TriggerBindingSource(ITriggeredFunctionBinding<TTriggerValue> functionBinding, TTriggerValue value)
        {
            _functionBinding = functionBinding;
            _value = value;
        }

        public IReadOnlyDictionary<string, IValueProvider> Bind(FunctionBindingContext context)
        {
            return _functionBinding.Bind(context, _value);
        }
    }
}
