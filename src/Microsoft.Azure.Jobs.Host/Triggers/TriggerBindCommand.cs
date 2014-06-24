using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class TriggerBindCommand<TTriggerValue> : IBindCommand
    {
        private readonly RuntimeBindingProviderContext _context;
        private readonly Guid _functionInstanceId;
        private readonly TTriggerValue _value;
        private readonly ITriggeredFunctionBinding<TTriggerValue> _functionBinding;

        public TriggerBindCommand(ITriggeredFunctionBinding<TTriggerValue> functionBinding,
            RuntimeBindingProviderContext context, Guid functionInstanceId, TTriggerValue value)
        {
            _functionBinding = functionBinding;
            _context = context;
            _functionInstanceId = functionInstanceId;
            _value = value;
        }

        public IReadOnlyDictionary<string, IValueProvider> Execute()
        {
            return _functionBinding.Bind(_context, _functionInstanceId, _value);
        }
    }
}
