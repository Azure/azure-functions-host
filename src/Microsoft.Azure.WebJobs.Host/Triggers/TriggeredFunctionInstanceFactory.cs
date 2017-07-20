// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Triggers
{
    internal class TriggeredFunctionInstanceFactory<TTriggerValue> : ITriggeredFunctionInstanceFactory<TTriggerValue>
    {
        private readonly ITriggeredFunctionBinding<TTriggerValue> _binding;
        private readonly IFunctionInvoker _invoker;
        private readonly FunctionDescriptor _descriptor;

        public TriggeredFunctionInstanceFactory(ITriggeredFunctionBinding<TTriggerValue> binding,
            IFunctionInvoker invoker, FunctionDescriptor descriptor)
        {
            _binding = binding;
            _invoker = invoker;
            _descriptor = descriptor;
        }

        public IFunctionInstance Create(FunctionInstanceFactoryContext<TTriggerValue> context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            IBindingSource bindingSource = new TriggerBindingSource<TTriggerValue>(_binding, context.TriggerValue);
            var invoker = CreateInvoker(context);

            return new FunctionInstance(Guid.NewGuid(), context.ParentId, ExecutionReason.AutomaticTrigger, bindingSource, invoker, _descriptor);
        }

        public IFunctionInstance Create(FunctionInstanceFactoryContext context)
        {
            IBindingSource bindingSource = new BindingSource(_binding, context.Parameters);
            var invoker = CreateInvoker(context);

            return new FunctionInstance(context.Id, context.ParentId, context.ExecutionReason, bindingSource, invoker, _descriptor);
        }

        private IFunctionInvoker CreateInvoker(FunctionInstanceFactoryContext context)
        {
            if (context.InvokeHandler != null)
            {
                return new InvokeWrapper(_invoker, context.InvokeHandler);
            }
            else
            {
                return _invoker;
            }
        }

        private class InvokeWrapper : IFunctionInvoker
        {
            private readonly IFunctionInvoker _inner;
            private readonly Func<Func<Task<object>>, Task<object>> _handler;

            public InvokeWrapper(IFunctionInvoker inner, Func<Func<Task<object>>, Task<object>> handler)
            {
                _inner = inner;
                _handler = handler;
            }
            public IReadOnlyList<string> ParameterNames => _inner.ParameterNames;

            public Task<object> InvokeAsync(object[] arguments)
            {
                Func<Task<object>> inner = () => _inner.InvokeAsync(arguments);
                return _handler(inner);
            }
        }
    }
}
