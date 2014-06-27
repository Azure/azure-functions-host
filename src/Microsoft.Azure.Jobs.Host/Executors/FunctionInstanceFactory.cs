using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class FunctionInstanceFactory : IFunctionInstanceFactory
    {
        private readonly IFunctionBinding _binding;
        private readonly FunctionDescriptor _descriptor;
        private readonly MethodInfo _method;

        public FunctionInstanceFactory(IFunctionBinding binding, FunctionDescriptor descriptor, MethodInfo method)
        {
            _binding = binding;
            _descriptor = descriptor;
            _method = method;
        }

        public IFunctionInstance Create(Guid id, Guid? parentId, ExecutionReason reason,
            IDictionary<string, object> parameters)
        {
            IBindingSource bindingSource = new BindingSource(_binding, parameters);
            return new FunctionInstance(id, parentId, reason, bindingSource, _descriptor, _method);
        }
    }
}
