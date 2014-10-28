// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal static class StorageQueueExtensions
    {
        public static void AddMessage(this IStorageQueue queue, IStorageQueueMessage message)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            queue.AddMessageAsync(message, CancellationToken.None).GetAwaiter().GetResult();
        }

        public static void CreateIfNotExists(this IStorageQueue queue)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            queue.CreateIfNotExistsAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public static bool Exists(this IStorageQueue queue)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            return queue.ExistsAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public static IStorageQueueMessage GetMessage(this IStorageQueue queue)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            IEnumerable<IStorageQueueMessage> messages = GetMessages(queue, messageCount: 1);

            if (messages == null)
            {
                return null;
            }

            return messages.SingleOrDefault();
        }

        public static IEnumerable<IStorageQueueMessage> GetMessages(this IStorageQueue queue, int messageCount)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            return queue.GetMessagesAsync(messageCount, visibilityTimeout: null, options: null, operationContext: null,
                cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
