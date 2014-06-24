using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;
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
        private readonly ITriggeredFunctionBinding<BrokeredMessage> _functionBinding;
        private readonly FunctionDescriptor _functionDescriptor;
        private readonly MethodInfo _method;

        public ServiceBusSubscriptionListenerFactory(ServiceBusAccount account, string topicName, string subscriptionName,
            ITriggeredFunctionBinding<BrokeredMessage> functionBinding, FunctionDescriptor functionDescriptor,
            MethodInfo method)
        {
            _namespaceManager = account.NamespaceManager;
            _messagingFactory = account.MessagingFactory;
            _topicName = topicName;
            _subscriptionName = subscriptionName;
            _functionBinding = functionBinding;
            _functionDescriptor = functionDescriptor;
            _method = method;
        }

        public IListener Create(IFunctionExecutor executor, RuntimeBindingProviderContext context)
        {
            CancellationToken cancellationToken = context.CancellationToken;

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            // Must create all messaging entities before creating message receivers and calling OnMessage.
            // Otherwise, some function could start to execute and try to output messages to entities that don't yet
            // exist.
            _namespaceManager.CreateTopicIfNotExists(_topicName);
            _namespaceManager.CreateSubscriptionIfNotExists(_topicName, _subscriptionName);

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            string entityPath = SubscriptionClient.FormatSubscriptionPath(_topicName, _subscriptionName);

            return new ServiceBusListener(_messagingFactory, entityPath, _functionBinding, _functionDescriptor, _method,
                executor, context);
        }
    }
}
