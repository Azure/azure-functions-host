using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.HostMessaging
{
    internal static class CredentialsDescriptorExtensions
    {
        public static string GetStorageConnectionString(this CredentialsDescriptor credentials)
        {
            if (credentials == null)
            {
                throw new ArgumentNullException("credentials");
            }

            IEnumerable<ConnectionStringDescriptor> connectionStrings = credentials.ConnectionStrings;

            if (connectionStrings == null)
            {
                return null;
            }

            StorageConnectionStringDescriptor descriptor =
                connectionStrings.OfType<StorageConnectionStringDescriptor>().FirstOrDefault();

            if (descriptor == null)
            {
                return null;
            }

            return descriptor.ConnectionString;
        }
    }
}
