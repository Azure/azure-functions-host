// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.ServiceBus;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    internal static class ServiceBusClient
    {
        public static string GetNamespaceName(ServiceBusAccount account)
        {
            if (account == null)
            {
                return null;
            }

            // TODO: FACAVAL - This needs to be refactored
            return string.Empty;
        }

        internal static string GetNamespaceName(Uri address)
        {
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
