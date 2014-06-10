using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Listeners
{
    internal static class NamespaceManagerExtensions
    {
        public static void CreateQueueIfNotExists(this NamespaceManager manager, string path)
        {
            if (!manager.QueueExists(path))
            {
                try
                {
                    manager.CreateQueue(path);
                }
                catch (MessagingEntityAlreadyExistsException)
                {
                }
            }
        }

        public static void CreateTopicIfNotExists(this NamespaceManager manager, string path)
        {
            if (!manager.TopicExists(path))
            {
                try
                {
                    manager.CreateTopic(path);
                }
                catch (MessagingEntityAlreadyExistsException)
                {
                }
            }
        }

        public static void CreateSubscriptionIfNotExists(this NamespaceManager manager, string topicPath, string name)
        {
            if (!manager.SubscriptionExists(topicPath, name))
            {
                try
                {
                    manager.CreateSubscription(topicPath, name);
                }
                catch (MessagingEntityAlreadyExistsException)
                {
                }
            }
        }
    }
}
