using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class ServiceBusTriggerBinding : ITriggerBinding<BrokeredMessage>
    {
        private static readonly IObjectToTypeConverter<BrokeredMessage> _converter =
            new OutputConverter<BrokeredMessage>(new IdentityConverter<BrokeredMessage>());

        private readonly IArgumentBinding<BrokeredMessage> _argumentBinding;
        private readonly string _queueName;
        private readonly string _topicName;
        private readonly string _subscriptionName;
        private readonly string _entityPath;

        public ServiceBusTriggerBinding(IArgumentBinding<BrokeredMessage> argumentBinding, string queueName)
        {
            _argumentBinding = argumentBinding;
            _queueName = queueName;
            _entityPath = queueName;
        }

        public ServiceBusTriggerBinding(IArgumentBinding<BrokeredMessage> argumentBinding, string topicName, string subscriptionName)
        {
            _argumentBinding = argumentBinding;
            _topicName = topicName;
            _subscriptionName = subscriptionName;
            _entityPath = SubscriptionClient.FormatSubscriptionPath(topicName, subscriptionName);
        }

        public IReadOnlyDictionary<string, Type> BindingDataContract
        {
            get { return BindingData.GetContract(_argumentBinding.ValueType); }
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

        public ITriggerData Bind(BrokeredMessage value, ArgumentBindingContext context)
        {
            BrokeredMessage clonedMessage = value.Clone();
            IValueProvider valueProvider = _argumentBinding.Bind(value, context);
            IReadOnlyDictionary<string, object> bindingData = CreateBindingData(clonedMessage);

            return new TriggerData(valueProvider, bindingData);
        }

        public ITriggerData Bind(object value, ArgumentBindingContext context)
        {
            BrokeredMessage message = null;

            if (!_converter.TryConvert(value, out message))
            {
                throw new InvalidOperationException("Unable to convert trigger to BrokeredMessage.");
            }

            return Bind(message, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new ServiceBusParameterDescriptor
            {
                EntityPath = _entityPath,
                IsInput = true
            };
        }

        private IReadOnlyDictionary<string, object> CreateBindingData(BrokeredMessage message)
        {
            string contents;

            using (Stream stream = message.GetBody<Stream>())
            using (TextReader reader = new StreamReader(stream))
            {
                contents = reader.ReadToEnd();
            }

            return BindingData.GetBindingData(contents, BindingDataContract);
        }
    }
}
