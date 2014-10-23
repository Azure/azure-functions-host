// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class ServiceBusTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        private static readonly IQueueTriggerArgumentBindingProvider _innerProvider =
            new CompositeArgumentBindingProvider(
                new ConverterArgumentBindingProvider<BrokeredMessage>(
                    new AsyncConverter<BrokeredMessage, BrokeredMessage>(new IdentityConverter<BrokeredMessage>())),
                new ConverterArgumentBindingProvider<string>(new BrokeredMessageToStringConverter()),
                new ConverterArgumentBindingProvider<byte[]>(new BrokeredMessageToByteArrayConverter()),
                new UserTypeArgumentBindingProvider()); // Must come last, because it will attempt to bind all types.

        private readonly INameResolver _nameResolver;
        private readonly IServiceBusAccountProvider _accountProvider;

        public ServiceBusTriggerAttributeBindingProvider(INameResolver nameResolver,
            IServiceBusAccountProvider accountProvider)
        {
            if (accountProvider == null)
            {
                throw new ArgumentNullException("accountProvider");
            }

            _nameResolver = nameResolver;
            _accountProvider = accountProvider;
        }

        public Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            ServiceBusTriggerAttribute serviceBusTrigger = parameter.GetCustomAttribute<ServiceBusTriggerAttribute>(inherit: false);

            if (serviceBusTrigger == null)
            {
                return Task.FromResult<ITriggerBinding>(null);
            }

            string queueName = null;
            string topicName = null;
            string subscriptionName = null;

            if (serviceBusTrigger.QueueName != null)
            {
                queueName = Resolve(serviceBusTrigger.QueueName);
            }
            else
            {
                topicName = Resolve(serviceBusTrigger.TopicName);
                subscriptionName = Resolve(serviceBusTrigger.SubscriptionName);
            }

            ITriggerDataArgumentBinding<BrokeredMessage> argumentBinding = _innerProvider.TryCreate(parameter);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind ServiceBusTrigger to type '" + parameter.ParameterType + "'.");
            }

            string connectionString = _accountProvider.ConnectionString;
            ServiceBusAccount account = ServiceBusAccount.CreateFromConnectionString(connectionString);
            ITriggerBinding binding;

            if (queueName != null)
            {
                binding = new ServiceBusTriggerBinding(parameter.Name, parameter.ParameterType, argumentBinding,
                    account, queueName);
            }
            else
            {
                binding = new ServiceBusTriggerBinding(parameter.Name, argumentBinding, account, topicName,
                    subscriptionName);
            }

            return Task.FromResult(binding);
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
