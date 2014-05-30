using System;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class ServiceBusEntity
    {
        public ServiceBusAccount Account { get; set; }

        public MessageSender MessageSender { get; set; }

        public void SendAndCreateQueueIfNotExists(BrokeredMessage message, Guid functionInstanceId)
        {
            MessageSender.SendAndCreateQueueIfNotExists(message, functionInstanceId, Account.NamespaceManager);
        }
    }
}
