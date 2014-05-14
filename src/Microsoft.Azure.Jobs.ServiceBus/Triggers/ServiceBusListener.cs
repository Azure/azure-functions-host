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

        public void PollServiceBus(CancellationToken token)
        {
            foreach (var kv in _mapServiceBus)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                var receiver = kv.Key;
                var funcs = kv.Value;

                BrokeredMessage msg = null;

                try
                {
                    msg = receiver.Receive(TimeSpan.Zero);
                }
                catch (MessagingEntityNotFoundException)
                {
                    EnsureMessagingEntityIsAvailable(funcs[0].AccountConnectionString, receiver.Path);
                    msg = receiver.Receive(TimeSpan.Zero);
                }

                if (msg != null)
                {
                    // TODO: implement lock renewal for servicebus messages
                    // using (IntervalSeparationTimer timer = CreateUpdateMessageVisibilityTimer(queue, msg, visibilityTimeout))
                    {
                        // timer.Start(executeFirst: false);

                        foreach (var func in funcs)
                        {
                            if (token.IsCancellationRequested)
                            {
                                return;
                            }

                            _invoker.OnNewServiceBusMessage(func, msg, token);
                        }
                    }

                    // Need to call Delete message only if function succeeded. 
                    // and that gets trickier when we have multiple funcs listening. 
                    try
                    {
                        msg.Complete();
                    }
                    catch (MessageLockLostException)
                    {
                        // Completed already, or abandoned and somebody else is working on it, we don't care either way.
                    }
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
            return MessagingFactory.CreateFromConnectionString(func.AccountConnectionString);
        }
    }
}