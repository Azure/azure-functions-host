using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class FunctionBinding : IFunctionBinding
    {
        private readonly MethodInfo _method;
        private readonly IReadOnlyDictionary<string, IBinding> _bindings;

        public FunctionBinding(MethodInfo method, IReadOnlyDictionary<string, IBinding> bindings)
        {
            _method = method;
            _bindings = bindings;
        }

        public IReadOnlyDictionary<string, IValueProvider> Bind(RuntimeBindingProviderContext context,
            Guid functionInstanceId, IDictionary<string, object> parameters)
        {
            Dictionary<string, IValueProvider> results = new Dictionary<string, IValueProvider>();
            IReadOnlyDictionary<string, object> bindingData = null;

            BindingContext bindingContext = new BindingContext
            {
                FunctionInstanceId = functionInstanceId,
                NotifyNewBlob = context.NotifyNewBlob,
                CancellationToken = context.CancellationToken,
                ConsoleOutput = context.ConsoleOutput,
                NameResolver = context.NameResolver,
                StorageAccount = context.StorageAccount,
                ServiceBusConnectionString = context.ServiceBusConnectionString,
                BindingData = bindingData,
                BindingProvider = context.BindingProvider
            };

            foreach (KeyValuePair<string, IBinding> item in _bindings)
            {
                string name = item.Key;
                IBinding binding = item.Value;
                IValueProvider valueProvider;

                try
                {
                    if (parameters != null && parameters.ContainsKey(name))
                    {
                        valueProvider = binding.Bind(parameters[name], bindingContext);
                    }
                    else
                    {
                        valueProvider = binding.Bind(bindingContext);
                    }

                }
                catch (Exception exception)
                {
                    valueProvider = new BindingExceptionValueProvider(name, exception);
                }

                results.Add(name, valueProvider);
            }

            return results;
        }
    }
}
