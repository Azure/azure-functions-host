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
        private static readonly IQueueTriggerArgumentBindingProvider _innerProvider =
            new CompositeArgumentBindingProvider(
                new ConverterArgumentBindingProvider<CloudQueueMessage>(new IdentityConverter<CloudQueueMessage>()),
                new ConverterArgumentBindingProvider<string>(new CloudQueueMessageToStringConverter()),
                new ConverterArgumentBindingProvider<byte[]>(new CloudQueueMessageToByteArrayConverter()),
                new UserTypeArgumentBindingProvider()); // Must come last, because it will attempt to bind all types.

        public ITriggerBinding TryCreate(TriggerBindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            QueueTriggerAttribute queueTrigger = parameter.GetCustomAttribute<QueueTriggerAttribute>(inherit: false);

            if (queueTrigger == null)
            {
                return null;
            }

            string queueName = context.Resolve(queueTrigger.QueueName);
            queueName = NormalizeAndValidate(queueName);

            IArgumentBinding<CloudQueueMessage> argumentBinding = _innerProvider.TryCreate(parameter);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind QueueTrigger to type '" + parameter.ParameterType + "'.");
            }

            return new QueueTriggerBinding(argumentBinding, queueName);
        }

        private static string NormalizeAndValidate(string queueName)
        {
            queueName = queueName.ToLowerInvariant(); // must be lowercase. coerce here to be nice.
            QueueClient.ValidateQueueName(queueName);
            return queueName;
        }
    }
}
