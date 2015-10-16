// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Triggers
{
    internal class TriggeredFunctionBinding<TTriggerValue> : ITriggeredFunctionBinding<TTriggerValue>
    {
        private readonly FunctionDescriptor _descriptor;
        private readonly string _triggerParameterName;
        private readonly ITriggerBinding _triggerBinding;
        private readonly IReadOnlyDictionary<string, IBinding> _nonTriggerBindings;
        private readonly SingletonManager _singletonManager;

        public TriggeredFunctionBinding(FunctionDescriptor descriptor, string triggerParameterName, ITriggerBinding triggerBinding,
            IReadOnlyDictionary<string, IBinding> nonTriggerBindings, SingletonManager singletonManager)
        {
            _descriptor = descriptor;
            _triggerParameterName = triggerParameterName;
            _triggerBinding = triggerBinding;
            _nonTriggerBindings = nonTriggerBindings;
            _singletonManager = singletonManager;
        }

        public async Task<IReadOnlyDictionary<string, IValueProvider>> BindAsync(ValueBindingContext context, TTriggerValue value)
        {
            return await BindCoreAsync(context, value, null);
        }

        public async Task<IReadOnlyDictionary<string, IValueProvider>> BindAsync(ValueBindingContext context,
            IDictionary<string, object> parameters)
        {
            if (parameters == null || !parameters.ContainsKey(_triggerParameterName))
            {
                throw new InvalidOperationException("Missing value for trigger parameter '" + _triggerParameterName + "'.");
            }

            object value = parameters[_triggerParameterName];
            return await BindCoreAsync(context, value, parameters);
        }

        private async Task<IReadOnlyDictionary<string, IValueProvider>> BindCoreAsync(ValueBindingContext context, object value, IDictionary<string, object> parameters)
        {
            Dictionary<string, IValueProvider> valueProviders = new Dictionary<string, IValueProvider>();
            IValueProvider triggerProvider;
            IReadOnlyDictionary<string, object> bindingData;

            try
            {
                ITriggerData triggerData = await _triggerBinding.BindAsync(value, context);
                triggerProvider = triggerData.ValueProvider;
                bindingData = triggerData.BindingData;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                triggerProvider = new BindingExceptionValueProvider(_triggerParameterName, exception);
                bindingData = null;
            }
            valueProviders.Add(_triggerParameterName, triggerProvider);

            BindingContext bindingContext = new BindingContext(context, bindingData);

            // Bind Singleton if specified
            SingletonAttribute singletonAttribute = SingletonManager.GetFunctionSingletonOrNull(_descriptor.Method, isTriggered: true);
            if (singletonAttribute != null)
            {
                string boundScope = _singletonManager.GetBoundScope(singletonAttribute.Scope, bindingData);
                IValueProvider singletonValueProvider = new SingletonValueProvider(_descriptor.Method, boundScope, context.FunctionInstanceId.ToString(), singletonAttribute, _singletonManager);
                valueProviders.Add(SingletonValueProvider.SingletonParameterName, singletonValueProvider);
            }

            foreach (KeyValuePair<string, IBinding> item in _nonTriggerBindings)
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

                valueProviders.Add(name, valueProvider);
            }

            return valueProviders;
        }
    }
}
