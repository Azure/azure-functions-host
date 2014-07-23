// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class TriggeredFunctionBinding<TTriggerValue> : ITriggeredFunctionBinding<TTriggerValue>
    {
        private readonly string _triggerParameterName;
        private readonly ITriggerBinding<TTriggerValue> _triggerBinding;
        private readonly IReadOnlyDictionary<string, IBinding> _nonTriggerBindings;

        public TriggeredFunctionBinding(string triggerParameterName, ITriggerBinding<TTriggerValue> triggerBinding,
            IReadOnlyDictionary<string, IBinding> nonTriggerBindings)
        {
            _triggerParameterName = triggerParameterName;
            _triggerBinding = triggerBinding;
            _nonTriggerBindings = nonTriggerBindings;
        }

        public async Task<IReadOnlyDictionary<string, IValueProvider>> BindAsync(FunctionBindingContext context,
            TTriggerValue value)
        {
            Dictionary<string, IValueProvider> results = new Dictionary<string, IValueProvider>();

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

            results.Add(_triggerParameterName, triggerProvider);

            BindingContext bindingContext = new BindingContext(context, bindingData);

            foreach (KeyValuePair<string, IBinding> item in _nonTriggerBindings)
            {
                string name = item.Key;
                IBinding binding = item.Value;
                IValueProvider valueProvider;

                try
                {
                    valueProvider = await binding.BindAsync(bindingContext);
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

        public async Task<IReadOnlyDictionary<string, IValueProvider>> BindAsync(FunctionBindingContext context,
            IDictionary<string, object> parameters)
        {
            Dictionary<string, IValueProvider> results = new Dictionary<string, IValueProvider>();

            if (parameters == null || !parameters.ContainsKey(_triggerParameterName))
            {
                throw new InvalidOperationException(
                    "Missing value for trigger parameter '" + _triggerParameterName + "'.");
            }

            object triggerValue = parameters[_triggerParameterName];

            IValueProvider triggerProvider;
            IReadOnlyDictionary<string, object> bindingData;

            try
            {
                ITriggerData triggerData = await _triggerBinding.BindAsync(triggerValue, context);
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

            results.Add(_triggerParameterName, triggerProvider);

            BindingContext bindingContext = new BindingContext(context, bindingData);

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

                results.Add(name, valueProvider);
            }

            return results;
        }
    }
}
