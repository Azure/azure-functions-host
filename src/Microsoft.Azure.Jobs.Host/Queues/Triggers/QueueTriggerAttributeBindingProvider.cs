using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    internal class QueueTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        static readonly ITypeToObjectConverter<CloudQueueMessage>[] _converters = new ITypeToObjectConverter<CloudQueueMessage>[]
        {
            new InputConverter<CloudQueueMessage>(new IdentityConverter<CloudQueueMessage>()),
            new InputConverter<string>(new CloudQueueMessageToStringConverter()),
            new InputConverter<byte[]>(new CloudQueueMessageToByteArrayConverter()),
            new InputConverter<object>(new CloudQueueMessageToObjectConverter()),
            new CloudQueueMessageToUserTypeConverter()
        };

        public ITriggerBinding TryCreate(TriggerBindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            QueueTriggerAttribute queueTrigger = parameter.GetCustomAttribute<QueueTriggerAttribute>();

            if (queueTrigger == null)
            {
                return null;
            }

            string queueName = context.Resolve(queueTrigger.QueueName);
            queueName = NormalizeAndValidate(queueName);

            ITypeToObjectConverter<CloudQueueMessage> converter = null;

            foreach (ITypeToObjectConverter<CloudQueueMessage> possibleConverter in _converters)
            {
                if (possibleConverter.CanConvert(parameter.ParameterType))
                {
                    converter = possibleConverter;
                    break;
                }
            }

            if (converter == null)
            {
                throw new InvalidOperationException("Can't bind QueueTrigger to type '" + parameter.ParameterType + "'.");
            }

            IArgumentBinding<CloudQueueMessage> argumentBinding =
                new QueueTriggerArgumentBinding(converter, parameter.ParameterType);
            return new QueueTriggerBinding(argumentBinding, queueName);
        }

        string NormalizeAndValidate(string queueName)
        {
            queueName = queueName.ToLowerInvariant(); // must be lowercase. coerce here to be nice.
            QueueClient.ValidateQueueName(queueName);
            return queueName;
        }
    }
}
