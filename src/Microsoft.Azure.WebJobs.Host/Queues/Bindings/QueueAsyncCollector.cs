// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class QueueAsyncCollector<T> : IAsyncCollector<T>
    {
        private readonly IStorageQueue _queue;
        private readonly IConverter<T, IStorageQueueMessage> _converter;

        public QueueAsyncCollector(IStorageQueue queue, IConverter<T, IStorageQueueMessage> converter)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            if (converter == null)
            {
                throw new ArgumentNullException("converter");
            }

            _queue = queue;
            _converter = converter;
        }

        public Task AddAsync(T item, CancellationToken cancellationToken)
        {
            IStorageQueueMessage message = _converter.Convert(item);

            if (message == null)
            {
                throw new InvalidOperationException("Cannot enqueue a null queue message instance.");
            }

            return _queue.AddMessageAndCreateIfNotExistsAsync(message, cancellationToken);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // Batching not supported. 
            return Task.FromResult(0);
        }
    }
}
