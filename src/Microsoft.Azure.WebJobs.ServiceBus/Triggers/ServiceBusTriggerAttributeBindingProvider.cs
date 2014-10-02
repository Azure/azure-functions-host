// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
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
                queueName = context.Resolve(serviceBusTrigger.QueueName);
            }
            else
            {
                topicName = context.Resolve(serviceBusTrigger.TopicName);
                subscriptionName = context.Resolve(serviceBusTrigger.SubscriptionName);
            }

            ITriggerDataArgumentBinding<BrokeredMessage> argumentBinding = _innerProvider.TryCreate(parameter);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind ServiceBusTrigger to type '" + parameter.ParameterType + "'.");
            }

            ServiceBusAccount account = ServiceBusAccount.CreateFromConnectionString(
                context.ServiceBusConnectionString);
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
    }
}
