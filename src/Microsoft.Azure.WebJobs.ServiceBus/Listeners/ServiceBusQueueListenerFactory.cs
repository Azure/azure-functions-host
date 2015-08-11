// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Listeners
{
    internal class ServiceBusQueueListenerFactory : IListenerFactory
    {
        private readonly NamespaceManager _namespaceManager;
        private readonly MessagingFactory _messagingFactory;
        private readonly string _queueName;
        private readonly ITriggeredFunctionExecutor _executor;
        private readonly AccessRights _accessRights;
        private readonly ServiceBusConfiguration _config;

        public ServiceBusQueueListenerFactory(ServiceBusAccount account, string queueName, ITriggeredFunctionExecutor executor, AccessRights accessRights, ServiceBusConfiguration config)
        {
            _namespaceManager = account.NamespaceManager;
            _messagingFactory = account.MessagingFactory;
            _queueName = queueName;
            _executor = executor;
            _accessRights = accessRights;
            _config = config;
        }

        public async Task<IListener> CreateAsync(CancellationToken cancellationToken)
        {
            if (_accessRights == AccessRights.Manage)
            {
                // Must create all messaging entities before creating message receivers and calling OnMessage.
                // Otherwise, some function could start to execute and try to output messages to entities that don't yet
                // exist.
                await _namespaceManager.CreateQueueIfNotExistsAsync(_queueName, cancellationToken);
            }

            ServiceBusTriggerExecutor triggerExecutor = new ServiceBusTriggerExecutor(_executor);
            return new ServiceBusListener(_messagingFactory, _queueName, triggerExecutor, _config);
        }
    }
}
