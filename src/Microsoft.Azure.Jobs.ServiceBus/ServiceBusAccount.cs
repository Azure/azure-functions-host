using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus
{
    internal class ServiceBusAccount
    {
        public MessagingFactory MessagingFactory { get; set; }

        public NamespaceManager NamespaceManager { get; set; }

        public static ServiceBusAccount CreateFromConnectionString(string connectionString)
        {
            return new ServiceBusAccount
            {
                NamespaceManager = NamespaceManager.CreateFromConnectionString(connectionString),
                MessagingFactory = MessagingFactory.CreateFromConnectionString(connectionString)
            };
        }
    }
}
