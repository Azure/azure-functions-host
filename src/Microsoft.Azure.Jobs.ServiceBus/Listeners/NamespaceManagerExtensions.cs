// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Listeners
{
    internal static class NamespaceManagerExtensions
    {
        public static async Task CreateQueueIfNotExistsAsync(this NamespaceManager manager, string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await manager.QueueExistsAsync(path))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await manager.CreateQueueAsync(path);
                }
                catch (MessagingEntityAlreadyExistsException)
                {
                }
            }
        }

        public static async Task CreateTopicIfNotExistsAsync(this NamespaceManager manager, string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await manager.TopicExistsAsync(path))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await manager.CreateTopicAsync(path);
                }
                catch (MessagingEntityAlreadyExistsException)
                {
                }
            }
        }

        public static async Task CreateSubscriptionIfNotExistsAsync(this NamespaceManager manager, string topicPath,
            string name, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await manager.SubscriptionExistsAsync(topicPath, name))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await manager.CreateSubscriptionAsync(topicPath, name);
                }
                catch (MessagingEntityAlreadyExistsException)
                {
                }
            }
        }
    }
}
