// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Listeners
{
    internal static class NamespaceManagerExtensions
    {
        private const string DeadLetterQueueSuffix = "$DeadLetterQueue";

        public static async Task CreateQueueIfNotExistsAsync(this NamespaceManager manager, string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string parentQueuePath = SplitQueuePath(path)[0];
            if (!await manager.QueueExistsAsync(parentQueuePath))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await manager.CreateQueueAsync(parentQueuePath);
                }
                catch (MessagingEntityAlreadyExistsException)
                {
                }
            }
        }

        /// <summary>
        /// Split queue or subscription path into parent and DLQ parts if the latter exists. 
        /// </summary>
        /// <param name="path">Not empty string with Azure ServiceBus queue or subscription path.</param>
        /// <returns>Array of strings, where the first mandatory element is a parent queue path 
        /// if given path ends with the DLQ suffix or the original queue path otherwise.</returns>
        public static string[] SplitQueuePath(string path)
        {
            if (string.IsNullOrEmpty(path)) 
            {
                throw new ArgumentException("path cannot be null or empty", "path");
            }

            if (path.Length > DeadLetterQueueSuffix.Length && path.EndsWith(DeadLetterQueueSuffix, StringComparison.OrdinalIgnoreCase)) 
            {
                return new string[] { 
                    path.Substring(0, path.Length - DeadLetterQueueSuffix.Length - 1), 
                    DeadLetterQueueSuffix };
            }
            
            return new string[]{path};
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

            string parentSubscriptionName = SplitQueuePath(name)[0];
            if (!await manager.SubscriptionExistsAsync(topicPath, parentSubscriptionName))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await manager.CreateSubscriptionAsync(topicPath, parentSubscriptionName);
                }
                catch (MessagingEntityAlreadyExistsException)
                {
                }
            }
        }
    }
}
