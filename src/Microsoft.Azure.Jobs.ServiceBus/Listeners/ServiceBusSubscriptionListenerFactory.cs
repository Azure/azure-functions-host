using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Listeners
{
    internal class ServiceBusSubscriptionListenerFactory : IListenerFactory
    {
        private readonly NamespaceManager _namespaceManager;
        private readonly MessagingFactory _messagingFactory;
        private readonly string _topicName;
        private readonly string _subscriptionName;
        private readonly ITriggeredFunctionInstanceFactory<BrokeredMessage> _instanceFactory;

        public ServiceBusSubscriptionListenerFactory(ServiceBusAccount account, string topicName,
            string subscriptionName, ITriggeredFunctionInstanceFactory<BrokeredMessage> instanceFactory)
        {
            _namespaceManager = account.NamespaceManager;
            _messagingFactory = account.MessagingFactory;
            _topicName = topicName;
            _subscriptionName = subscriptionName;
            _instanceFactory = instanceFactory;
        }

        public IListener Create(IFunctionExecutor executor, ListenerFactoryContext context)
        {
            // Must create all messaging entities before creating message receivers and calling OnMessage.
            // Otherwise, some function could start to execute and try to output messages to entities that don't yet
            // exist.
            _namespaceManager.CreateTopicIfNotExists(_topicName);
            _namespaceManager.CreateSubscriptionIfNotExists(_topicName, _subscriptionName);

            string entityPath = SubscriptionClient.FormatSubscriptionPath(_topicName, _subscriptionName);

            ITriggerExecutor<BrokeredMessage> triggerExecutor = new ServiceBusTriggerExecutor(_instanceFactory, executor);
            return new ServiceBusListener(_messagingFactory, entityPath, triggerExecutor);
        }
    }
}
