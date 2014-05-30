using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class QueueAttributeBindingProvider : IBindingProvider
    {
        private static readonly IQueueArgumentBindingProvider _innerProvider = new CompositeArgumentBindingProvider(
            new CloudQueueArgumentBindingProvider(),
            new CloudQueueMessageArgumentBindingProvider(),
            new StringArgumentBindingProvider(),
            new ByteArrayArgumentBindingProvider(),
            new CollectionArgumentBindingProvider(),
            new UserTypeArgumentBindingProvider()); // Must come after collection provider (IEnumerable checks).

        public IBinding TryCreate(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            QueueAttribute queueAttribute = parameter.GetCustomAttribute<QueueAttribute>();

            if (queueAttribute == null)
            {
                return null;
            }

            string queueName = context.Resolve(queueAttribute.QueueName);
            queueName = NormalizeAndValidate(queueAttribute.QueueName);

            IArgumentBinding<CloudQueue> argumentBinding = _innerProvider.TryCreate(parameter);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind Queue to type '" + parameter.ParameterType + "'.");
            }

            CloudQueue queue = context.StorageAccount.CreateCloudQueueClient().GetQueueReference(queueName);

            return new QueueBinding(argumentBinding, queue);
        }

        private static string NormalizeAndValidate(string queueName)
        {
            queueName = queueName.ToLowerInvariant(); // must be lowercase. coerce here to be nice.
            QueueClient.ValidateQueueName(queueName);
            return queueName;
        }
    }
}
