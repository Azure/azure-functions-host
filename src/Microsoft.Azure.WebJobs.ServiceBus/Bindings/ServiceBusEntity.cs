// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    internal class ServiceBusEntity
    {
        public ServiceBusAccount Account { get; set; }

        public MessageSender MessageSender { get; set; }

        public AccessRights AccessRights { get; set; }

        public Task SendAndCreateQueueIfNotExistsAsync(BrokeredMessage message, Guid functionInstanceId, CancellationToken cancellationToken)
        {
            return MessageSender.SendAndCreateQueueIfNotExistsAsync(message, functionInstanceId,
                Account.NamespaceManager, AccessRights, cancellationToken);
        }
    }
}
