// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Triggers
{
    internal class StringToStorageQueueMessageConverter : IConverter<string, IStorageQueueMessage>
    {
        private readonly IStorageQueue _queue;

        public StringToStorageQueueMessageConverter(IStorageQueue queue)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            _queue = queue;
        }

        public IStorageQueueMessage Convert(string input)
        {
            return _queue.CreateMessage(input);
        }
    }
}
