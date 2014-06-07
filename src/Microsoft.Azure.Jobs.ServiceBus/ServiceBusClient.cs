using System;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus
{
    internal static class ServiceBusClient
    {
        public static string GetNamespaceName(ServiceBusAccount account)
        {
            if (account == null)
            {
                return null;
            }

            return GetNamespaceName(account.MessagingFactory);
        }

        public static string GetNamespaceName(MessagingFactory factory)
        {
            if (factory == null)
            {
                return null;
            }

            Uri address = factory.Address;

            if (address == null && !address.IsAbsoluteUri && address.HostNameType != UriHostNameType.Dns)
            {
                return null;
            }

            return GetLeastSignificantSubdomain(address.Host);
        }

        private static string GetLeastSignificantSubdomain(string host)
        {
            if (String.IsNullOrEmpty(host))
            {
                return null;
            }

            int separatorIndex = host.IndexOf('.');

            if (separatorIndex <= 0)
            {
                return null;
            }

            return host.Substring(0, separatorIndex);
        }
    }
}
