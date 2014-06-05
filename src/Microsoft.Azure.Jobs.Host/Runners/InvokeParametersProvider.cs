using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Triggers;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class InvokeParametersProvider : IParametersProvider
    {
        private readonly Guid _functionInstanceId;
        private readonly FunctionDefinition _function;
        private readonly IDictionary<string, object> _parameters;
        private readonly RuntimeBindingProviderContext _context;

        public InvokeParametersProvider(Guid functionInstanceId, FunctionDefinition function,
            IDictionary<string, object> parameters, RuntimeBindingProviderContext context)
        {
            _functionInstanceId = functionInstanceId;
            _function = function;
            _parameters = parameters;
            _context = context;
        }

        public FunctionDefinition Function
        {
            get { return _function; }
        }

        public IReadOnlyDictionary<string, IValueProvider> Bind()
        {
            Dictionary<string, IValueProvider> parameters = new Dictionary<string, IValueProvider>();
            IReadOnlyDictionary<string, object> bindingData;

            string triggerParameterName = _function.TriggerParameterName;

            if (triggerParameterName != null)
            {
                if (_parameters == null || !_parameters.ContainsKey(triggerParameterName))
                {
                    throw new InvalidOperationException(
                        "Missing value for trigger parameter '" + triggerParameterName + "'.");
                }

                object triggerValue = _parameters[triggerParameterName];

                ArgumentBindingContext triggerContext = new ArgumentBindingContext
                {
                    FunctionInstanceId = _functionInstanceId,
                    NotifyNewBlob = _context.NotifyNewBlob,
                    CancellationToken = _context.CancellationToken,
                    ConsoleOutput = _context.ConsoleOutput,
                    NameResolver = _context.NameResolver,
                    StorageAccount = _context.StorageAccount,
                    ServiceBusConnectionString = _context.ServiceBusConnectionString
                };

                IValueProvider triggerProvider;

                try
                {
                    ITriggerData triggerData = _function.TriggerBinding.Bind(triggerValue, triggerContext);
                    triggerProvider = triggerData.ValueProvider;
                    bindingData = triggerData.BindingData;
                }
                catch (Exception exception)
                {
                    triggerProvider = new BindingExceptionValueProvider(triggerParameterName, exception);
                    bindingData = null;
                }

                parameters.Add(triggerParameterName, triggerProvider);
            }
            else
            {
                bindingData = null;
            }

            BindingContext bindingContext = new BindingContext
            {
                FunctionInstanceId = _functionInstanceId,
                NotifyNewBlob = _context.NotifyNewBlob,
                CancellationToken = _context.CancellationToken,
                ConsoleOutput = _context.ConsoleOutput,
                NameResolver = _context.NameResolver,
                StorageAccount = _context.StorageAccount,
                ServiceBusConnectionString = _context.ServiceBusConnectionString,
                BindingData = bindingData,
                BindingProvider = _context.BindingProvider
            };

            foreach (KeyValuePair<string, IBinding> item in _function.NonTriggerBindings)
            {
                string name = item.Key;
                IBinding binding = item.Value;
                IValueProvider valueProvider;

                try
                {
                    if (_parameters != null && _parameters.ContainsKey(name))
                    {
                        valueProvider = binding.Bind(_parameters[name], bindingContext);
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

                parameters.Add(name, valueProvider);
            }

            return parameters;
        }
    }
}
