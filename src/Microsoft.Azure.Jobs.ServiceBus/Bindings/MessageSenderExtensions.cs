// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.ServiceBus.Listeners;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal static class MessageSenderExtensions
    {
        public static async Task SendAndCreateQueueIfNotExistsAsync(this MessageSender sender, BrokeredMessage message,
            Guid functionInstanceId, NamespaceManager namespaceManager, CancellationToken cancellationToken)
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
                threwMessgingEntityNotFoundException = true;
            }

            Debug.Assert(threwMessgingEntityNotFoundException);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await namespaceManager.CreateQueueAsync(sender.Path);
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
