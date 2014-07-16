// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.ServiceBus;

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
            ServiceBusConnectionStringBuilder builder = new ServiceBusConnectionStringBuilder(connectionString);

            HashSet<Uri> endpoints = builder.Endpoints;

            if (endpoints == null || endpoints.Count == 0)
            {
                return null;
            }

            Uri endpoint = endpoints.First();

            if (endpoint == null)
            {
                return null;
            }

            return ServiceBusClient.GetNamespaceName(endpoint);
        }
    }
}
