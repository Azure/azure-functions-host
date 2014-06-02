using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class ServiceBusAccount
    {
        public MessagingFactory MessagingFactory { get; set; }

        public NamespaceManager NamespaceManager { get; set; }
    }
}
