// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Listeners
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

        public async Task<IListener> CreateAsync(IFunctionExecutor executor, ListenerFactoryContext context)
        {
            // Must create all messaging entities before creating message receivers and calling OnMessage.
            // Otherwise, some function could start to execute and try to output messages to entities that don't yet
            // exist.
            await _namespaceManager.CreateQueueIfNotExistsAsync(_queueName, context.CancellationToken);

            ITriggerExecutor<BrokeredMessage> triggerExecutor = new ServiceBusTriggerExecutor(_instanceFactory, executor);
            return new ServiceBusListener(_messagingFactory, _queueName, triggerExecutor);
        }
    }
}
