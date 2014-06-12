using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Listeners
{
    internal class ServiceBusListener
    {
        private readonly List<ServiceBusTrigger> _serviceBusTriggers = new List<ServiceBusTrigger>();
        private readonly List<MessageReceiver> _receivers = new List<MessageReceiver>();
        private readonly ServiceBusInvoker _invoker;

        public ServiceBusListener(Worker worker)
        {
            _invoker = new ServiceBusInvoker(worker);
        }

        public void Map(ServiceBusTrigger serviceBusTrigger)
        {
            if (serviceBusTrigger != null)
            {
                _serviceBusTriggers.Add(serviceBusTrigger);
            }
        }

        public void StartPollingServiceBus(RuntimeBindingProviderContext context)
        {
            if (_serviceBusTriggers.Count == 0)
            {
                return;
            }

            CancellationToken cancellationToken = context.CancellationToken;
            ServiceBusAccount account = ServiceBusAccount.CreateFromConnectionString(context.ServiceBusConnectionString);

            foreach (ServiceBusTrigger trigger in _serviceBusTriggers)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // Must create all messaging entities before creating message receivers and calling OnMessage.
                // Otherwise, some function could start to execute and try to output messages to entities that don't yet
                // exist.
                CreateMessagingEntityIfNotExists(account, trigger.SourcePath);
            }

            foreach (ServiceBusTrigger trigger in _serviceBusTriggers)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                MessageReceiver receiver = account.MessagingFactory.CreateMessageReceiver(trigger.SourcePath);
                _receivers.Add(receiver); // Prevent the receiver from being garbage collected.

                cancellationToken.Register(receiver.Close);
                receiver.OnMessage(m => Process(m, trigger, context), new OnMessageOptions());
            }
        }

        private void Process(BrokeredMessage message, ServiceBusTrigger trigger, RuntimeBindingProviderContext context)
        {
            CancellationToken cancellationToken = context.CancellationToken;

            if (cancellationToken.IsCancellationRequested)
            {
                message.Abandon();
                return;
            }

            _invoker.OnNewServiceBusMessage(trigger, message, context);

            // The preceding OnNewServiceBusMessage call may have returned without throwing an exception because the
            // cancellation token was triggered. We're not sure. To be safe, don't treat the message as successfully
            // processed unless we're positive that it has been (to guarantee our at-least-once semantics). If the
            // cancellation token is signaled now, assume the previous call terminated early.

            if (cancellationToken.IsCancellationRequested)
            {
                message.Abandon();
                return;
            }
        }

        private static void CreateMessagingEntityIfNotExists(ServiceBusAccount account, string entityPath)
        {
            NamespaceManager manager = account.NamespaceManager;

            if (entityPath.Contains("/Subscriptions/"))
            {
                var parts = entityPath.Split(new[] { "/Subscriptions/" }, StringSplitOptions.None);
                var topic = parts[0];
                var subscription = parts[1];
                manager.CreateTopicIfNotExists(topic);
                manager.CreateSubscriptionIfNotExists(topic, subscription);
            }
            else
            {
                manager.CreateQueueIfNotExists(entityPath);
            }
        }
    }
}
