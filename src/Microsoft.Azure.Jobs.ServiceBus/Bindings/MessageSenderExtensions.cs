using System;
using Microsoft.Azure.Jobs.ServiceBus.Listeners;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal static class MessageSenderExtensions
    {
        public static void SendAndCreateQueueIfNotExists(this MessageSender sender, BrokeredMessage message,
            Guid functionInstanceId, NamespaceManager namespaceManager)
        {
            if (sender == null)
            {
                throw new ArgumentNullException("sender");
            }
            else if (namespaceManager == null)
            {
                throw new ArgumentNullException("namespaceManager");
            }

            ServiceBusCausalityHelper.EncodePayload(functionInstanceId, message);

            try
            {
                sender.Send(message);
            }
            catch (MessagingEntityNotFoundException)
            {
                try
                {
                    namespaceManager.CreateQueue(sender.Path);
                }
                catch (MessagingEntityAlreadyExistsException)
                {
                }

                // Clone the message because it was already consumed before (when trying to send)
                // otherwise, you get an exception
                message = message.Clone();
                sender.Send(message);
            }
        }
    }
}
