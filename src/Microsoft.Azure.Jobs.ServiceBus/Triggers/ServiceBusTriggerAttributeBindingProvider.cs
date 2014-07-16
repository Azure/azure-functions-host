// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class ServiceBusTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        private static readonly IQueueTriggerArgumentBindingProvider _innerProvider =
            new CompositeArgumentBindingProvider(
                new ConverterArgumentBindingProvider<BrokeredMessage>(new IdentityConverter<BrokeredMessage>()),
                new ConverterArgumentBindingProvider<string>(new BrokeredMessageToStringConverter()),
                new ConverterArgumentBindingProvider<byte[]>(new BrokeredMessageToByteArrayConverter()),
                new UserTypeArgumentBindingProvider()); // Must come last, because it will attempt to bind all types.

        public ITriggerBinding TryCreate(TriggerBindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            ServiceBusTriggerAttribute serviceBusTrigger = parameter.GetCustomAttribute<ServiceBusTriggerAttribute>(inherit: false);

            if (serviceBusTrigger == null)
            {
                return null;
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

            IArgumentBinding<BrokeredMessage> argumentBinding = _innerProvider.TryCreate(parameter);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind ServiceBusTrigger to type '" + parameter.ParameterType + "'.");
            }

            ServiceBusAccount account = ServiceBusAccount.CreateFromConnectionString(
                context.ServiceBusConnectionString);

            if (queueName != null)
            {
                return new ServiceBusTriggerBinding(parameter.Name, argumentBinding, account, queueName);
            }
            else
            {
                return new ServiceBusTriggerBinding(parameter.Name, argumentBinding, account, topicName, subscriptionName);
            }
        }
    }
}
