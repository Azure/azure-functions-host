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
        private static readonly ITypeToObjectConverter<BrokeredMessage>[] _converters = new ITypeToObjectConverter<BrokeredMessage>[]
        {
            new InputConverter<BrokeredMessage>(new IdentityConverter<BrokeredMessage>()),
            new InputConverter<string>(new BrokeredMessageToStringConverter()),
            new InputConverter<byte[]>(new BrokeredMessageToByteArrayConverter()),
            new BrokeredMessageToUserTypeConverter()
        };

        public ITriggerBinding TryCreate(TriggerBindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            ServiceBusTriggerAttribute serviceBusTrigger = parameter.GetCustomAttribute<ServiceBusTriggerAttribute>();

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

            ITypeToObjectConverter<BrokeredMessage> converter = null;

            foreach (ITypeToObjectConverter<BrokeredMessage> possibleConverter in _converters)
            {
                if (possibleConverter.CanConvert(parameter.ParameterType))
                {
                    converter = possibleConverter;
                    break;
                }
            }

            if (converter == null)
            {
                throw new InvalidOperationException("Can't bind ServiceBusTrigger to type '" + parameter.ParameterType + "'.");
            }

            IArgumentBinding<BrokeredMessage> argumentBinding =
                new ServiceBusTriggerArgumentBinding(converter, parameter.ParameterType);

            if (queueName != null)
            {
                return new ServiceBusTriggerBinding(argumentBinding, queueName);
            }
            else
            {
                return new ServiceBusTriggerBinding(argumentBinding, topicName, subscriptionName);
            }
        }
    }
}
