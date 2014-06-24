using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class TriggeredFunctionBinding<TTriggerValue> : ITriggeredFunctionBinding<TTriggerValue>
    {
        private readonly MethodInfo _method;
        private readonly string _triggerParameterName;
        private readonly ITriggerBinding<TTriggerValue> _triggerBinding;
        private readonly IReadOnlyDictionary<string, IBinding> _nonTriggerBindings;

        public TriggeredFunctionBinding(MethodInfo method, string triggerParameterName,
            ITriggerBinding<TTriggerValue> triggerBinding, IReadOnlyDictionary<string, IBinding> nonTriggerBindings)
        {
            _method = method;
            _triggerParameterName = triggerParameterName;
            _triggerBinding = triggerBinding;
            _nonTriggerBindings = nonTriggerBindings;
        }

        public IReadOnlyDictionary<string, IValueProvider> Bind(RuntimeBindingProviderContext context,
            Guid functionInstanceId, TTriggerValue value)
        {
            Dictionary<string, IValueProvider> results = new Dictionary<string, IValueProvider>();

            ArgumentBindingContext triggerContext = CreateTriggerContext(context, functionInstanceId);

            IValueProvider triggerProvider;
            IReadOnlyDictionary<string, object> bindingData;

            try
            {
                ITriggerData triggerData = _triggerBinding.Bind(value, triggerContext);
                triggerProvider = triggerData.ValueProvider;
                bindingData = triggerData.BindingData;
            }
            catch (Exception exception)
            {
                triggerProvider = new BindingExceptionValueProvider(_triggerParameterName, exception);
                bindingData = null;
            }

            results.Add(_triggerParameterName, triggerProvider);

            BindingContext bindingContext = CreateBindingContext(context, functionInstanceId, bindingData);

            foreach (KeyValuePair<string, IBinding> item in _nonTriggerBindings)
            {
                string name = item.Key;
                IBinding binding = item.Value;
                IValueProvider valueProvider;

                try
                {
                    valueProvider = binding.Bind(bindingContext);
                }
                catch (Exception exception)
                {
                    valueProvider = new BindingExceptionValueProvider(name, exception);
                }

                results.Add(name, valueProvider);
            }

            return results;
        }

        public IReadOnlyDictionary<string, IValueProvider> Bind(RuntimeBindingProviderContext context,
            Guid functionInstanceId, IDictionary<string, object> parameters)
        {
            Dictionary<string, IValueProvider> results = new Dictionary<string, IValueProvider>();

            if (parameters == null || !parameters.ContainsKey(_triggerParameterName))
            {
                throw new InvalidOperationException(
                    "Missing value for trigger parameter '" + _triggerParameterName + "'.");
            }

            object triggerValue = parameters[_triggerParameterName];

            ArgumentBindingContext triggerContext = CreateTriggerContext(context, functionInstanceId);

            IValueProvider triggerProvider;
            IReadOnlyDictionary<string, object> bindingData;

            try
            {
                ITriggerData triggerData = _triggerBinding.Bind(triggerValue, triggerContext);
                triggerProvider = triggerData.ValueProvider;
                bindingData = triggerData.BindingData;
            }
            catch (Exception exception)
            {
                triggerProvider = new BindingExceptionValueProvider(_triggerParameterName, exception);
                bindingData = null;
            }

            results.Add(_triggerParameterName, triggerProvider);

            BindingContext bindingContext = CreateBindingContext(context, functionInstanceId, bindingData);

            foreach (KeyValuePair<string, IBinding> item in _nonTriggerBindings)
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

        private static ArgumentBindingContext CreateTriggerContext(RuntimeBindingProviderContext context,
            Guid functionInstanceId)
        {
            return new ArgumentBindingContext
            {
                FunctionInstanceId = functionInstanceId,
                NotifyNewBlob = context.NotifyNewBlob,
                CancellationToken = context.CancellationToken,
                ConsoleOutput = context.ConsoleOutput,
                NameResolver = context.NameResolver,
                StorageAccount = context.StorageAccount,
                ServiceBusConnectionString = context.ServiceBusConnectionString
            };
        }

        private static BindingContext CreateBindingContext(RuntimeBindingProviderContext context,
            Guid functionInstanceId, IReadOnlyDictionary<string, object> bindingData)
        {
            return new BindingContext
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
        }
    }
}
