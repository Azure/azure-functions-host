// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.ServiceBus;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class ServiceBusTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        private static readonly IQueueTriggerArgumentBindingProvider InnerProvider =
            new CompositeArgumentBindingProvider(
                new ConverterArgumentBindingProvider<Message>(
                    new AsyncConverter<BrokeredMessage, Message>(new IdentityConverter<Message>())),
                new ConverterArgumentBindingProvider<string>(new BrokeredMessageToStringConverter()),
                new ConverterArgumentBindingProvider<byte[]>(new BrokeredMessageToByteArrayConverter()),
                new UserTypeArgumentBindingProvider()); // Must come last, because it will attempt to bind all types.

        private readonly INameResolver _nameResolver;
        private readonly ServiceBusConfiguration _config;

        public ServiceBusTriggerAttributeBindingProvider(INameResolver nameResolver, ServiceBusConfiguration config)
        {
            if (nameResolver == null)
            {
                throw new ArgumentNullException("nameResolver");
            }
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _nameResolver = nameResolver;
            _config = config;
        }

        public Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            ParameterInfo parameter = context.Parameter;
            var attribute = TypeUtility.GetResolvedAttribute<ServiceBusTriggerAttribute>(parameter);

            if (attribute == null)
            {
                return Task.FromResult<ITriggerBinding>(null);
            }

            string queueName = null;
            string topicName = null;
            string subscriptionName = null;
            string entityPath = null;

            if (attribute.QueueName != null)
            {
                queueName = Resolve(attribute.QueueName);
                entityPath = queueName;
            }
            else
            {
                topicName = Resolve(attribute.TopicName);
                subscriptionName = Resolve(attribute.SubscriptionName);
                entityPath = SubscriptionClient.FormatSubscriptionPath(topicName, subscriptionName);
            }

            ITriggerDataArgumentBinding<Message> argumentBinding = InnerProvider.TryCreate(parameter);
            if (argumentBinding == null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "Can't bind ServiceBusTrigger to type '{0}'.", parameter.ParameterType));
            }

            ServiceBusAccount account = new ServiceBusAccount
            {
                MessagingFactory = _config.MessagingProvider.CreateMessagingFactory(entityPath, attribute.Connection),
            };

            ITriggerBinding binding;
            if (queueName != null)
            {
                binding = new ServiceBusTriggerBinding(parameter.Name, parameter.ParameterType, argumentBinding, account, _config, queueName);
            }
            else
            {
                binding = new ServiceBusTriggerBinding(parameter.Name, parameter.ParameterType, argumentBinding, account, _config, topicName, subscriptionName);
            }

            return Task.FromResult<ITriggerBinding>(binding);
        }

        private string Resolve(string queueName)
        {
            if (_nameResolver == null)
            {
                return queueName;
            }

            return _nameResolver.ResolveWholeString(queueName);
        }
    }
}
