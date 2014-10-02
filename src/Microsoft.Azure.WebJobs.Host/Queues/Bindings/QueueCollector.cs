// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class QueueCollector<T> : ICollector<T>
    {
        private readonly IStorageQueue _queue;
        private readonly IConverter<T, IStorageQueueMessage> _converter;

        public QueueCollector(IStorageQueue queue, IConverter<T, IStorageQueueMessage> converter)
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

        public void Add(T item)
        {
            IStorageQueueMessage message = _converter.Convert(item);

            if (message == null)
            {
                throw new InvalidOperationException("Cannot enqueue a null queue message instance.");
            }

            _queue.AddMessageAndCreateIfNotExistsAsync(message, CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
