using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class TriggeredFunctionInstanceFactory<TTriggerValue> : ITriggeredFunctionInstanceFactory<TTriggerValue>
    {
        private readonly ITriggeredFunctionBinding<TTriggerValue> _binding;
        private readonly FunctionDescriptor _descriptor;
        private readonly MethodInfo _method;

        public TriggeredFunctionInstanceFactory(ITriggeredFunctionBinding<TTriggerValue> binding,
            FunctionDescriptor descriptor, MethodInfo method)
        {
            _binding = binding;
            _descriptor = descriptor;
            _method = method;
        }

        public IFunctionInstance Create(TTriggerValue value, Guid? parentId)
        {
            IBindingSource bindingSource = new TriggerBindingSource<TTriggerValue>(_binding, value);
            return new FunctionInstance(Guid.NewGuid(), parentId, ExecutionReason.AutomaticTrigger, bindingSource,
                _descriptor, _method);
        }

        public IFunctionInstance Create(Guid id, Guid? parentId, ExecutionReason reason,
            IDictionary<string, object> parameters)
        {
            IBindingSource bindingSource = new BindingSource(_binding, parameters);
            return new FunctionInstance(id, parentId, reason, bindingSource, _descriptor, _method);
        }
    }
}
