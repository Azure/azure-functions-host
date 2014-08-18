// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    internal class ServiceBusAccount
    {
        public MessagingFactory MessagingFactory { get; set; }

        public NamespaceManager NamespaceManager { get; set; }

        public static ServiceBusAccount CreateFromConnectionString(string connectionString)
        {
            ServiceBusConnectionStringBuilder builder = new ServiceBusConnectionStringBuilder(connectionString);

            return new ServiceBusAccount
            {
                NamespaceManager = NamespaceManager.CreateFromConnectionString(connectionString),
                MessagingFactory = MessagingFactory.CreateFromConnectionString(connectionString)
            };
        }
    }
}
