// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Queues
{
    internal static class StorageQueueExtensions
    {
        public static async Task AddMessageAndCreateIfNotExistsAsync(this IStorageQueue queue,
            IStorageQueueMessage message, CancellationToken cancellationToken)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            bool isQueueNotFoundException = false;

            try
            {
                await queue.AddMessageAsync(message, cancellationToken);
                return;
            }
            catch (StorageException exception)
            {
                if (!exception.IsNotFoundQueueNotFound())
                {
                    throw;
                }

                isQueueNotFoundException = true;
            }

            Debug.Assert(isQueueNotFoundException);
            await queue.CreateIfNotExistsAsync(cancellationToken);
            await queue.AddMessageAsync(message, cancellationToken);
        }
    }
}
