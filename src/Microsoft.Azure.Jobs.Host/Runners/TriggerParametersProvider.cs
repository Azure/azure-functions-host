using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Host.Triggers;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class TriggerParametersProvider<TTriggerValue> : IParametersProvider
    {
        private readonly Guid _functionInstanceId;
        private readonly string _triggerParameterName;
        private readonly ITriggerBinding<TTriggerValue> _triggerBinding;
        private readonly TTriggerValue _triggerValue;
        private readonly IReadOnlyDictionary<string, IBinding> _nonTriggerBindings;
        private readonly RuntimeBindingProviderContext _context;

        public TriggerParametersProvider(Guid functionInstanceId, FunctionDefinition function,
            TTriggerValue triggerValue, RuntimeBindingProviderContext context)
        {
            _functionInstanceId = functionInstanceId;
            _triggerParameterName = function.TriggerParameterName;
            _triggerBinding = (ITriggerBinding<TTriggerValue>)function.TriggerBinding;
            _triggerValue = triggerValue;
            _nonTriggerBindings = function.NonTriggerBindings;
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

            ArgumentBindingContext triggerContext = new ArgumentBindingContext
            {
                FunctionInstanceId = _functionInstanceId,
                NotifyNewBlob = _context.NotifyNewBlob,
                CancellationToken = _context.CancellationToken,
                ConsoleOutput = _context.ConsoleOutput,
                NameResolver= _context.NameResolver,
                StorageAccount = _context.StorageAccount,
                ServiceBusConnectionString = _context.ServiceBusConnectionString
            };

            IValueProvider triggerProvider;
            IReadOnlyDictionary<string, object> bindingData;

            try
            {
                ITriggerData triggerData = _triggerBinding.Bind(_triggerValue, triggerContext);
                triggerProvider = triggerData.ValueProvider;
                bindingData = triggerData.BindingData;
            }
            catch (Exception exception)
            {
                triggerProvider = new BindingExceptionValueProvider(_triggerParameterName, exception);
                bindingData = null;
            }

            parameters.Add(_triggerParameterName, triggerProvider);

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
                    valueProvider = binding.Bind(bindingContext);
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
