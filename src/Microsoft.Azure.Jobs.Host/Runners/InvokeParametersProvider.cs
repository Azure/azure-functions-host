using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Triggers;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class InvokeParametersProvider : IParametersProvider
    {
        private readonly Guid _functionInstanceId;
        private readonly string _triggerParameterName;
        private readonly ITriggerBinding _triggerBinding;
        private readonly IReadOnlyDictionary<string, IBinding> _nonTriggerBindings;
        private readonly IDictionary<string, object> _parameters;
        private readonly RuntimeBindingProviderContext _context;

        public InvokeParametersProvider(Guid functionInstanceId, FunctionDefinition function,
            IDictionary<string, object> parameters, RuntimeBindingProviderContext context)
        {
            _functionInstanceId = functionInstanceId;
            _triggerParameterName = function.TriggerParameterName;
            _triggerBinding = function.TriggerBinding;
            _nonTriggerBindings = function.NonTriggerBindings;
            _parameters = parameters;
            _context = context;
        }

        public string TriggerParameterName
        {
            get { return _triggerParameterName; }
        }

        public ITriggerBinding TriggerBinding
        {
            get { return _triggerBinding; }
        }

        public IReadOnlyDictionary<string, IBinding> NonTriggerBindings
        {
            get { return _nonTriggerBindings; }
        }

        public IReadOnlyDictionary<string, IValueProvider> Bind()
        {
            Dictionary<string, IValueProvider> parameters = new Dictionary<string, IValueProvider>();
            IReadOnlyDictionary<string, object> bindingData;

            if (_triggerParameterName != null)
            {
                if (_parameters.ContainsKey(_triggerParameterName))
                {
                    throw new InvalidOperationException(
                        "Missing value for trigger parameter '" + _triggerParameterName + "'.");
                }

                object triggerValue = _parameters[_triggerParameterName];

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
                    ITriggerData triggerData = _triggerBinding.Bind(triggerValue, triggerContext);
                    triggerProvider = triggerData.ValueProvider;
                    bindingData = triggerData.BindingData;
                }
                catch (Exception exception)
                {
                    triggerProvider = new BindingExceptionValueProvider(_triggerParameterName, exception);
                    bindingData = null;
                }

                parameters.Add(_triggerParameterName, triggerProvider);
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

            foreach (KeyValuePair<string, IBinding> item in _nonTriggerBindings)
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
