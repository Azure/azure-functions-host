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
        private readonly FunctionDefinition _function;
        private readonly ITriggerBinding<TTriggerValue> _triggerBinding;
        private readonly TTriggerValue _triggerValue;
        private readonly RuntimeBindingProviderContext _context;

        public TriggerParametersProvider(Guid functionInstanceId, FunctionDefinition function,
            TTriggerValue triggerValue, RuntimeBindingProviderContext context)
        {
            _functionInstanceId = functionInstanceId;
            _function = function;
            _triggerBinding = (ITriggerBinding<TTriggerValue>)function.TriggerBinding;
            _triggerValue = triggerValue;
            _context = context;
        }

        public FunctionDefinition Function
        {
            get { return _function ; }
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
            string triggerParameterName = _function.TriggerParameterName;

            try
            {
                ITriggerData triggerData = _triggerBinding.Bind(_triggerValue, triggerContext);
                triggerProvider = triggerData.ValueProvider;
                bindingData = triggerData.BindingData;
            }
            catch (Exception exception)
            {
                triggerProvider = new BindingExceptionValueProvider(triggerParameterName, exception);
                bindingData = null;
            }

            parameters.Add(triggerParameterName, triggerProvider);

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
