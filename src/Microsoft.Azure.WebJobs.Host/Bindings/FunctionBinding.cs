// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class FunctionBinding : IFunctionBinding
    {
        private readonly FunctionDescriptor _descriptor;
        private readonly IReadOnlyDictionary<string, IBinding> _bindings;
        private readonly SingletonManager _singletonManager;

        public FunctionBinding(FunctionDescriptor descriptor, IReadOnlyDictionary<string, IBinding> bindings, SingletonManager singletonManager)
        {
            _descriptor = descriptor;
            _bindings = bindings;
            _singletonManager = singletonManager;
        }

        public async Task<IReadOnlyDictionary<string, IValueProvider>> BindAsync(ValueBindingContext context, IDictionary<string, object> parameters)
        {
            Dictionary<string, IValueProvider> results = new Dictionary<string, IValueProvider>();

            BindingContext bindingContext = new BindingContext(context, null);

            // bind Singleton if specified
            SingletonAttribute singletonAttribute = _descriptor.Method.GetCustomAttribute<SingletonAttribute>();
            if (singletonAttribute != null)
            {
                IValueProvider singletonValueProvider = new SingletonValueProvider(_descriptor.Method, singletonAttribute.Scope, context.FunctionInstanceId.ToString(), singletonAttribute, _singletonManager);
                results.Add(SingletonValueProvider.SingletonParameterName, singletonValueProvider);
            }

            foreach (KeyValuePair<string, IBinding> item in _bindings)
            {
                string name = item.Key;
                IBinding binding = item.Value;
                IValueProvider valueProvider;

                try
                {
                    if (parameters != null && parameters.ContainsKey(name))
                    {
                        valueProvider = await binding.BindAsync(parameters[name], context);
                    }
                    else
                    {
                        valueProvider = await binding.BindAsync(bindingContext);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
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
