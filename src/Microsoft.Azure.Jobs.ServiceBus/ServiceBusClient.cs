// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.ServiceBus.Messaging;

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

            return GetNamespaceName(account.MessagingFactory);
        }

        public static string GetNamespaceName(MessagingFactory factory)
        {
            if (factory == null)
            {
                return null;
            }

            return GetNamespaceName(factory.Address);
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
