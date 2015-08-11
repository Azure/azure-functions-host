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
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class ServiceBusTriggerBinding : ITriggerBinding
    {
        private readonly string _parameterName;
        private readonly IObjectToTypeConverter<BrokeredMessage> _converter;
        private readonly ITriggerDataArgumentBinding<BrokeredMessage> _argumentBinding;
        private readonly ServiceBusAccount _account;
        private readonly string _namespaceName;
        private readonly string _queueName;
        private readonly string _topicName;
        private readonly string _subscriptionName;
        private readonly string _entityPath;
        private readonly AccessRights _accessRights;
        private readonly ServiceBusConfiguration _config;

        public ServiceBusTriggerBinding(string parameterName, Type parameterType, 
            ITriggerDataArgumentBinding<BrokeredMessage> argumentBinding, ServiceBusAccount account, string queueName, AccessRights accessRights, ServiceBusConfiguration config)
        {
            _parameterName = parameterName;
            _converter = CreateConverter(parameterType);
            _argumentBinding = argumentBinding;
            _account = account;
            _namespaceName = ServiceBusClient.GetNamespaceName(account);
            _queueName = queueName;
            _entityPath = queueName;
            _accessRights = accessRights;
            _config = config;
        }

        public ServiceBusTriggerBinding(string parameterName, ITriggerDataArgumentBinding<BrokeredMessage> argumentBinding,
            ServiceBusAccount account, string topicName, string subscriptionName, AccessRights accessRights, ServiceBusConfiguration config)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _account = account;
            _namespaceName = ServiceBusClient.GetNamespaceName(account);
            _topicName = topicName;
            _subscriptionName = subscriptionName;
            _entityPath = SubscriptionClient.FormatSubscriptionPath(topicName, subscriptionName);
            _accessRights = accessRights;
            _config = config;
        }

        public Type TriggerValueType
        {
            get
            {
                return typeof(BrokeredMessage);
            }
        }

        public IReadOnlyDictionary<string, Type> BindingDataContract
        {
            get { return _argumentBinding.BindingDataContract; }
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
            BrokeredMessage message = value as BrokeredMessage;
            if (message == null && !_converter.TryConvert(value, out message))
            {
                throw new InvalidOperationException("Unable to convert trigger to BrokeredMessage.");
            }

            return await _argumentBinding.BindAsync(message, context);
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
                factory = new ServiceBusQueueListenerFactory(_account, _queueName, context.Executor, _accessRights, _config);
            }
            else
            {
                factory = new ServiceBusSubscriptionListenerFactory(_account, _topicName, _subscriptionName, context.Executor, _accessRights, _config);
            }
            return factory.CreateAsync(context.CancellationToken);
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

        private static IObjectToTypeConverter<BrokeredMessage> CreateConverter(Type parameterType)
        {
            return new CompositeObjectToTypeConverter<BrokeredMessage>(
                    new OutputConverter<BrokeredMessage>(new IdentityConverter<BrokeredMessage>()),
                    new OutputConverter<string>(StringToBrokeredMessageConverterFactory.Create(parameterType)));
        }
    }
}
