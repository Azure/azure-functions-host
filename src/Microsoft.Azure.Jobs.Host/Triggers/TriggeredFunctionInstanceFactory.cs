using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class TriggeredFunctionInstanceFactory<TTriggerValue> : ITriggeredFunctionInstanceFactory<TTriggerValue>
    {
        private readonly ITriggeredFunctionBinding<TTriggerValue> _functionBinding;
        private readonly FunctionDescriptor _descriptor;
        private readonly MethodInfo _method;

        public TriggeredFunctionInstanceFactory(ITriggeredFunctionBinding<TTriggerValue> functionBinding,
            FunctionDescriptor descriptor, MethodInfo method)
        {
            _functionBinding = functionBinding;
            _descriptor = descriptor;
            _method = method;
        }

        public IFunctionInstance Create(TTriggerValue value, Guid? parentId)
        {
            IBindingSource bindingSource = new TriggerBindingSource<TTriggerValue>(_functionBinding, value);
            return new FunctionInstance(Guid.NewGuid(), parentId, ExecutionReason.AutomaticTrigger, bindingSource,
                _descriptor, _method);
        }
    }
}
