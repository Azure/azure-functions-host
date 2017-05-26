// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.WebJobs.ServiceBus.Bindings;
using Microsoft.Azure.WebJobs.ServiceBus.Listeners;
using Microsoft.Azure.ServiceBus;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class ServiceBusTriggerBinding : ITriggerBinding
    {
        private readonly string _parameterName;
        private readonly IObjectToTypeConverter<Message> _converter;
        private readonly ITriggerDataArgumentBinding<Message> _argumentBinding;
        private readonly IReadOnlyDictionary<string, Type> _bindingDataContract;
        private readonly ServiceBusAccount _account;
        private readonly string _namespaceName;
        private readonly string _queueName;
        private readonly string _topicName;
        private readonly string _subscriptionName;
        private readonly string _entityPath;
        private readonly ServiceBusConfiguration _config;

        public ServiceBusTriggerBinding(string parameterName, Type parameterType, ITriggerDataArgumentBinding<Message> argumentBinding, ServiceBusAccount account,
            ServiceBusConfiguration config, string queueName)
            : this(parameterName, parameterType, argumentBinding, account, config)
        {
            _queueName = queueName;
            _entityPath = queueName;
        }

        public ServiceBusTriggerBinding(string parameterName, Type parameterType, ITriggerDataArgumentBinding<Message> argumentBinding, ServiceBusAccount account,
            ServiceBusConfiguration config, string topicName, string subscriptionName)
            : this(parameterName, parameterType, argumentBinding, account, config)
        {
            _topicName = topicName;
            _subscriptionName = subscriptionName;
            _entityPath = SubscriptionClient.FormatSubscriptionPath(topicName, subscriptionName);
        }

        private ServiceBusTriggerBinding(string parameterName, Type parameterType, ITriggerDataArgumentBinding<Message> argumentBinding, 
            ServiceBusAccount account, ServiceBusConfiguration config) 
        {
            _parameterName = parameterName;
            _converter = CreateConverter(parameterType);
            _argumentBinding = argumentBinding;
            _bindingDataContract = CreateBindingDataContract(argumentBinding.BindingDataContract);
            _account = account;
            _namespaceName = ServiceBusClient.GetNamespaceName(account);
            _config = config;
        }

        public Type TriggerValueType
        {
            get
            {
                return typeof(Message);
            }
        }

        public IReadOnlyDictionary<string, Type> BindingDataContract
        {
            get { return _bindingDataContract; }
        }

        public string QueueName
        {
            get { return _queueName; }
        }

        public string TopicName
        {
            get { return _topicName; }
        }

        public string SubscriptionName
        {
            get { return _subscriptionName; }
        }

        public string EntityPath
        {
            get { return _entityPath; }
        }

        public async Task<ITriggerData> BindAsync(object value, ValueBindingContext context)
        {
            Message message = value as Message;
            if (message == null && !_converter.TryConvert(value, out message))
            {
                throw new InvalidOperationException("Unable to convert trigger to BrokeredMessage.");
            }

            ITriggerData triggerData = await _argumentBinding.BindAsync(message, context);
            IReadOnlyDictionary<string, object> bindingData = CreateBindingData(message, triggerData.BindingData);

            return new TriggerData(triggerData.ValueProvider, bindingData);
        }

        public Task<IListener> CreateListenerAsync(ListenerFactoryContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            IListenerFactory factory = null;
            if (_queueName != null)
            {
                factory = new ServiceBusQueueListenerFactory(_account, _queueName, context.Executor, _config);
            }
            else
            {
                factory = new ServiceBusSubscriptionListenerFactory(_account, _topicName, _subscriptionName, context.Executor, _config);
            }
            return factory.CreateAsync(context.CancellationToken);
        }

        internal static IReadOnlyDictionary<string, Type> CreateBindingDataContract(IReadOnlyDictionary<string, Type> argumentBindingContract)
        {
            var contract = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            contract.Add("DeliveryCount", typeof(int));
            contract.Add("DeadLetterSource", typeof(string));
            contract.Add("ExpiresAtUtc", typeof(DateTime));
            contract.Add("EnqueuedTimeUtc", typeof(DateTime));
            contract.Add("MessageId", typeof(string));
            contract.Add("ContentType", typeof(string));
            contract.Add("ReplyTo", typeof(string));
            contract.Add("SequenceNumber", typeof(long));
            contract.Add("To", typeof(string));
            contract.Add("Label", typeof(string));
            contract.Add("CorrelationId", typeof(string));
            contract.Add("Properties", typeof(IDictionary<string, object>));

            if (argumentBindingContract != null)
            {
                foreach (KeyValuePair<string, Type> item in argumentBindingContract)
                {
                    // In case of conflict, binding data from the value type overrides the built-in binding data above.
                    contract[item.Key] = item.Value;
                }
            }

            return contract;
        }

        internal static IReadOnlyDictionary<string, object> CreateBindingData(Message value,
            IReadOnlyDictionary<string, object> bindingDataFromValueType)
        {
            var bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            SafeAddValue(() => bindingData.Add(nameof(value.SystemProperties.DeliveryCount), value.SystemProperties.DeliveryCount));
            SafeAddValue(() => bindingData.Add(nameof(value.DeadLetterSource), value.DeadLetterSource));
            SafeAddValue(() => bindingData.Add(nameof(value.ExpiresAtUtc), value.ExpiresAtUtc));
            SafeAddValue(() => bindingData.Add(nameof(value.SystemProperties.EnqueuedTimeUtc), value.SystemProperties.EnqueuedTimeUtc));
            SafeAddValue(() => bindingData.Add(nameof(value.MessageId), value.MessageId));
            SafeAddValue(() => bindingData.Add(nameof(value.ContentType), value.ContentType));
            SafeAddValue(() => bindingData.Add(nameof(value.ReplyTo), value.ReplyTo));
            SafeAddValue(() => bindingData.Add(nameof(value.SystemProperties.SequenceNumber), value.SystemProperties.SequenceNumber));
            SafeAddValue(() => bindingData.Add(nameof(value.To), value.To));
            SafeAddValue(() => bindingData.Add(nameof(value.Label), value.Label));
            SafeAddValue(() => bindingData.Add(nameof(value.CorrelationId), value.CorrelationId));
            SafeAddValue(() => bindingData.Add(nameof(value.UserProperties), value.UserProperties));

            if (bindingDataFromValueType != null)
            {
                foreach (KeyValuePair<string, object> item in bindingDataFromValueType)
                {
                    // In case of conflict, binding data from the value type overrides the built-in binding data above.
                    bindingData[item.Key] = item.Value;
                }
            }

            return bindingData;
        }

        private static void SafeAddValue(Action addValue)
        {
            try
            {
                addValue();
            }
            catch
            {
                // some message propery getters can throw, based on the
                // state of the message
            }
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            string entityPath = _queueName != null ?
                    _queueName : string.Format(CultureInfo.InvariantCulture, "{0}/Subscriptions/{1}", _topicName, _subscriptionName);

            return new ServiceBusTriggerParameterDescriptor
            {
                Name = _parameterName,
                NamespaceName = _namespaceName,
                QueueName = _queueName,
                TopicName = _topicName,
                SubscriptionName = _subscriptionName,
                DisplayHints = ServiceBusBinding.CreateParameterDisplayHints(entityPath, true)
            };
        }

        private static IObjectToTypeConverter<Message> CreateConverter(Type parameterType)
        {
            return new CompositeObjectToTypeConverter<Message>(
                    new OutputConverter<Message>(new IdentityConverter<Message>()),
                    new OutputConverter<string>(StringToBrokeredMessageConverterFactory.Create(parameterType)));
        }
    }
}
