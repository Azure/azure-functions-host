// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class ByteArrayToStorageQueueMessageConverter : IConverter<byte[], IStorageQueueMessage>
    {
        private readonly IStorageQueue _queue;

        public ByteArrayToStorageQueueMessageConverter(IStorageQueue queue)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            _queue = queue;
        }

        public IStorageQueueMessage Convert(byte[] input)
        {
            if (input == null)
            {
                throw new InvalidOperationException("A queue message cannot contain a null byte array instance.");
            }

            return _queue.CreateMessage(input);
        }
    }
}
