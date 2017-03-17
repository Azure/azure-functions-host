// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.ServiceBus.Listeners;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    internal static class MessageSenderExtensions
    {
        public static async Task SendAndCreateEntityIfNotExists(this MessageSender sender, BrokeredMessage message,
            Guid functionInstanceId, NamespaceManager namespaceManager, AccessRights accessRights, EntityType entityType, CancellationToken cancellationToken)
        {
            if (sender == null)
            {
                throw new ArgumentNullException("sender");
            }
            else if (namespaceManager == null)
            {
                throw new ArgumentNullException("namespaceManager");
            }

            ServiceBusCausalityHelper.EncodePayload(functionInstanceId, message);

            bool threwMessgingEntityNotFoundException = false;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await sender.SendAsync(message);
                return;
            }
            catch (MessagingEntityNotFoundException)
            {
                if (accessRights != AccessRights.Manage)
                {
                    // if we don't have the required rights to create the entity
                    // rethrow the exception
                    throw;
                }

                threwMessgingEntityNotFoundException = true;
            }

            Debug.Assert(threwMessgingEntityNotFoundException);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                switch (entityType)
                {
                    case EntityType.Topic:
                        await namespaceManager.CreateTopicAsync(sender.Path);
                        break;

                    case EntityType.Queue:
                    default:
                        await namespaceManager.CreateQueueAsync(sender.Path);
                        break;
                }
            }
            catch (MessagingEntityAlreadyExistsException)
            {
            }

            // Clone the message because it was already consumed before (when trying to send)
            // otherwise, you get an exception
            message = message.Clone();
            cancellationToken.ThrowIfCancellationRequested();
            await sender.SendAsync(message);
        }
    }
}
