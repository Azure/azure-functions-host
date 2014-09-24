// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
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
    }
}
