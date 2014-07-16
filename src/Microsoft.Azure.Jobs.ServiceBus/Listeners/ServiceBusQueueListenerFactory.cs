// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Listeners
{
    internal class ServiceBusQueueListenerFactory : IListenerFactory
    {
        private readonly NamespaceManager _namespaceManager;
        private readonly MessagingFactory _messagingFactory;
        private readonly string _queueName;
        private readonly ITriggeredFunctionInstanceFactory<BrokeredMessage> _instanceFactory;

        public ServiceBusQueueListenerFactory(ServiceBusAccount account, string queueName,
            ITriggeredFunctionInstanceFactory<BrokeredMessage> instanceFactory)
        {
            _namespaceManager = account.NamespaceManager;
            _messagingFactory = account.MessagingFactory;
            _queueName = queueName;
            _instanceFactory = instanceFactory;
        }

        public IListener Create(IFunctionExecutor executor, ListenerFactoryContext context)
        {
            // Must create all messaging entities before creating message receivers and calling OnMessage.
            // Otherwise, some function could start to execute and try to output messages to entities that don't yet
            // exist.
            _namespaceManager.CreateQueueIfNotExists(_queueName);

            ITriggerExecutor<BrokeredMessage> triggerExecutor = new ServiceBusTriggerExecutor(_instanceFactory, executor);
            return new ServiceBusListener(_messagingFactory, _queueName, triggerExecutor);
        }
    }
}
