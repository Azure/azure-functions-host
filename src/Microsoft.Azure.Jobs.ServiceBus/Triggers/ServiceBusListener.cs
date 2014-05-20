using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs
{
    internal class ServiceBusListener
    {
        private readonly IDictionary<MessageReceiver, List<ServiceBusTrigger>> _mapServiceBus;
        private readonly ServiceBusInvoker _invoker;

        public ServiceBusListener(Worker worker)
        {
            _invoker = new ServiceBusInvoker(worker);
            _mapServiceBus = new Dictionary<MessageReceiver, List<ServiceBusTrigger>>(new MessageReceiverComparer());
        }

        public void StartPollingServiceBus(CancellationToken token)
        {
            foreach (var kv in _mapServiceBus)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                var receiver = kv.Key;
                var triggers = kv.Value;

                token.Register(receiver.Close);
                try
                {
                    receiver.OnMessage(m => Process(m, triggers, token), new OnMessageOptions());
                }
                catch (MessagingEntityNotFoundException)
                {
                    EnsureMessagingEntityIsAvailable(triggers[0].StorageConnectionString, receiver.Path);
                    receiver.OnMessage(m => Process(m, triggers, token), new OnMessageOptions());
                }
            }
        }

        private void Process(BrokeredMessage message, IEnumerable<ServiceBusTrigger> triggers, CancellationToken cancellationToken)
        {
            foreach (ServiceBusTrigger trigger in triggers)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    message.Abandon();
                    return;
                }

                _invoker.OnNewServiceBusMessage(trigger, message, cancellationToken);

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
        }

        // call create on the entity path (queue, or topic+subscription) and ignore AlreadyExists exceptions,
        // to ensure that the entity is available.
        private static void EnsureMessagingEntityIsAvailable(string connectionString, string entityPath)
        {
            var manager = NamespaceManager.CreateFromConnectionString(connectionString);
            if (entityPath.Contains("/Subscriptions/"))
            {
                var parts = entityPath.Split(new[] { "/Subscriptions/" }, StringSplitOptions.None);
                var topic = parts[0];
                var subscription = parts[1];
                try
                {
                    manager.CreateTopic(topic);
                }
                catch (MessagingEntityAlreadyExistsException)
                {
                }
                try
                {
                    manager.CreateSubscription(topic, subscription);
                }
                catch (MessagingEntityAlreadyExistsException)
                {
                }
            }
            else
            {
                try
                {
                    manager.CreateQueue(entityPath);
                }
                catch (MessagingEntityAlreadyExistsException)
                {
                }
            }
        }

        public void Map(ServiceBusTrigger serviceBusTrigger)
        {
            if (serviceBusTrigger != null)
            {
                var factory = GetMessagingFactory(serviceBusTrigger);
                var receiver = factory.CreateMessageReceiver(serviceBusTrigger.SourcePath);
                _mapServiceBus.GetOrCreate(receiver).Add(serviceBusTrigger);
            }
        }

        private MessagingFactory GetMessagingFactory(Trigger func)
        {
            return MessagingFactory.CreateFromConnectionString(func.StorageConnectionString);
        }
    }
}