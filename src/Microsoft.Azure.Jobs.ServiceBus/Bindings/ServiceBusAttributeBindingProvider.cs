using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class ServiceBusAttributeBindingProvider : IBindingProvider
    {
        private static readonly IServiceBusArgumentBindingProvider _innerProvider =
            new CompositeArgumentBindingProvider(
                new BrokeredMessageArgumentBindingProvider(),
                new StringArgumentBindingProvider(),
                new ByteArrayArgumentBindingProvider(),
                new CollectionArgumentBindingProvider(),
                new UserTypeArgumentBindingProvider()); // Must be after collection provider (IEnumerable checks).

        public IBinding TryCreate(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            ServiceBusAttribute serviceBusAttribute = parameter.GetCustomAttribute<ServiceBusAttribute>();

            if (serviceBusAttribute == null)
            {
                return null;
            }

            string queueOrTopicName = context.Resolve(serviceBusAttribute.QueueOrTopicName);

            IArgumentBinding<ServiceBusEntity> argumentBinding = _innerProvider.TryCreate(parameter);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind ServiceBus to type '" + parameter.ParameterType + "'.");
            }

            string connectionString = context.ServiceBusConnectionString;
            MessagingFactory messagingFactory = MessagingFactory.CreateFromConnectionString(connectionString);

            ServiceBusEntity entity = new ServiceBusEntity
            {
                Account = new ServiceBusAccount
                {
                    NamespaceManager = NamespaceManager.CreateFromConnectionString(connectionString),
                    MessagingFactory = messagingFactory
                },
                MessageSender = messagingFactory.CreateMessageSender(queueOrTopicName)
            };

            return new ServiceBusBinding(argumentBinding, entity);
        }
    }
}
