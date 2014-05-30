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

            string queueName = context.Resolve(serviceBusTrigger.QueueName);
            queueName = NormalizeAndValidate(queueName);

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
            return new ServiceBusTriggerBinding(argumentBinding, queueName);
        }

        private static string NormalizeAndValidate(string queueName)
        {
            queueName = queueName.ToLowerInvariant(); // must be lowercase. coerce here to be nice.
            QueueClient.ValidateQueueName(queueName);
            return queueName;
        }
    }
}
