// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.WebJobs.ServiceBus.Config;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class ServiceBusTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        private static readonly IQueueTriggerArgumentBindingProvider InnerProvider =
            new CompositeArgumentBindingProvider(
                new ConverterArgumentBindingProvider<BrokeredMessage>(
                    new AsyncConverter<BrokeredMessage, BrokeredMessage>(new IdentityConverter<BrokeredMessage>())),
                new ConverterArgumentBindingProvider<string>(new BrokeredMessageToStringConverter()),
                new ConverterArgumentBindingProvider<byte[]>(new BrokeredMessageToByteArrayConverter()),
                new UserTypeArgumentBindingProvider()); // Must come last, because it will attempt to bind all types.

        private readonly INameResolver _nameResolver;
        private readonly TraceWriter _trace;
        private readonly ServiceBusConfiguration _config;

        public ServiceBusTriggerAttributeBindingProvider(INameResolver nameResolver, TraceWriter trace, ServiceBusConfiguration config)
        {
            if (nameResolver == null)
            {
                throw new ArgumentNullException("nameResolver");
            }
            if (trace == null)
            {
                throw new ArgumentNullException("trace");
            }
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _nameResolver = nameResolver;
            _trace = trace;
            _config = config;
        }

        public async Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            ParameterInfo parameter = context.Parameter;
            ServiceBusTriggerAttribute attribute = parameter.GetCustomAttribute<ServiceBusTriggerAttribute>(inherit: false);

            if (attribute == null)
            {
                return null;
            }

            string queueName = null;
            string topicName = null;
            string subscriptionName = null;

            if (attribute.QueueName != null)
            {
                queueName = Resolve(attribute.QueueName);
            }
            else
            {
                topicName = Resolve(attribute.TopicName);
                subscriptionName = Resolve(attribute.SubscriptionName);
            }

            ITriggerDataArgumentBinding<BrokeredMessage> argumentBinding = InnerProvider.TryCreate(parameter);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "Can't bind ServiceBusTrigger to type '{0}'.", parameter.ParameterType));
            }
            
            ITriggerBinding binding;
            if (queueName != null)
            {
                ServiceBusAccount account = new ServiceBusAccount
                {
                    MessagingFactory = await _config.MessagingProvider.CreateMessagingFactoryAsync(queueName),
                    NamespaceManager = _config.MessagingProvider.CreateNamespaceManager(queueName)
                };
                binding = new ServiceBusTriggerBinding(parameter.Name, parameter.ParameterType, argumentBinding, account, queueName, attribute.Access, _trace, _config);
            }
            else
            {
                string entityPath = SubscriptionClient.FormatSubscriptionPath(topicName, subscriptionName);
                ServiceBusAccount account = new ServiceBusAccount
                {
                    MessagingFactory = await _config.MessagingProvider.CreateMessagingFactoryAsync(entityPath),
                    NamespaceManager = _config.MessagingProvider.CreateNamespaceManager(entityPath)
                };
                binding = new ServiceBusTriggerBinding(parameter.Name, argumentBinding, account, topicName, subscriptionName, attribute.Access, _trace, _config);
            }

            return binding;
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
