// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.Azure.Jobs.ServiceBus.Listeners;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class ServiceBusTriggerBinding : ITriggerBinding<BrokeredMessage>
    {
        private static readonly IObjectToTypeConverter<BrokeredMessage> _converter =
            new CompositeObjectToTypeConverter<BrokeredMessage>(
                new OutputConverter<BrokeredMessage>(new IdentityConverter<BrokeredMessage>()),
                new OutputConverter<string>(new StringToBrokeredMessageConverter()));

        private readonly string _parameterName;
        private readonly IArgumentBinding<BrokeredMessage> _argumentBinding;
        private readonly ServiceBusAccount _account;
        private readonly string _namespaceName;
        private readonly string _queueName;
        private readonly string _topicName;
        private readonly string _subscriptionName;
        private readonly string _entityPath;

        public ServiceBusTriggerBinding(string parameterName, IArgumentBinding<BrokeredMessage> argumentBinding,
            ServiceBusAccount account, string queueName)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _account = account;
            _namespaceName = ServiceBusClient.GetNamespaceName(account);
            _queueName = queueName;
            _entityPath = queueName;
        }

        public ServiceBusTriggerBinding(string parameterName, IArgumentBinding<BrokeredMessage> argumentBinding, ServiceBusAccount account,
            string topicName, string subscriptionName)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _account = account;
            _namespaceName = ServiceBusClient.GetNamespaceName(account);
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

        public ITriggerData Bind(BrokeredMessage value, FunctionBindingContext context)
        {
            BrokeredMessage clonedMessage = value.Clone();
            IValueProvider valueProvider = _argumentBinding.Bind(value, context);
            IReadOnlyDictionary<string, object> bindingData = CreateBindingData(clonedMessage);

            return new TriggerData(valueProvider, bindingData);
        }

        public ITriggerData Bind(object value, FunctionBindingContext context)
        {
            BrokeredMessage message = null;

            if (!_converter.TryConvert(value, out message))
            {
                throw new InvalidOperationException("Unable to convert trigger to BrokeredMessage.");
            }

            return Bind(message, context);
        }

        public IFunctionDefinition CreateFunctionDefinition(IReadOnlyDictionary<string, IBinding> nonTriggerBindings,
            FunctionDescriptor functionDescriptor, MethodInfo method)
        {
            ITriggeredFunctionBinding<BrokeredMessage> functionBinding =
                new TriggeredFunctionBinding<BrokeredMessage>(_parameterName, this, nonTriggerBindings);
            ITriggeredFunctionInstanceFactory<BrokeredMessage> instanceFactory =
                new TriggeredFunctionInstanceFactory<BrokeredMessage>(functionBinding, functionDescriptor, method);
            IListenerFactory listenerFactory;

            if (_queueName != null)
            {
                listenerFactory = new ServiceBusQueueListenerFactory(_account, _queueName, instanceFactory);
            }
            else
            {
                listenerFactory = new ServiceBusSubscriptionListenerFactory(_account, _topicName, _subscriptionName,
                    instanceFactory);
            }

            return new FunctionDefinition(instanceFactory, listenerFactory);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new ServiceBusTriggerParameterDescriptor
            {
                Name = _parameterName,
                NamespaceName = _namespaceName,
                QueueName = _queueName,
                TopicName = _topicName,
                SubscriptionName = _subscriptionName
            };
        }

        private IReadOnlyDictionary<string, object> CreateBindingData(BrokeredMessage clonedMessage)
        {
            string contents;

            using (Stream stream = clonedMessage.GetBody<Stream>())
            {
                if (stream == null)
                {
                    return null;
                }

                try
                {
                    using (TextReader reader = new StreamReader(stream, StrictEncodings.Utf8))
                    {
                        contents = reader.ReadToEnd();
                    }
                }
                catch (DecoderFallbackException)
                {
                    return null;
                }
            }

            return BindingData.GetBindingData(contents, BindingDataContract);
        }
    }
}
