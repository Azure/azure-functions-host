using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Listeners
{
    internal static class ServiceBusConnectionStringDescriptorFactory
    {
        public static ConnectionStringDescriptor Create(string connectionString)
        {
            string serviceBusNamespace = GetNamespaceName(connectionString);

            if (serviceBusNamespace == null)
            {
                return null;
            }

            return new ServiceBusConnectionStringDescriptor
            {
                Namespace = serviceBusNamespace,
                ConnectionString = connectionString
            };
        }

        private static string GetNamespaceName(string connectionString)
        {
            MessagingFactory factory;

            try
            {
                factory = MessagingFactory.CreateFromConnectionString(connectionString);
            }
            catch
            {
                return null;
            }

            return ServiceBusClient.GetNamespaceName(factory);
        }
    }
}
