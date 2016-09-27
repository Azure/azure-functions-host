// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    /// <summary>
    /// Core object to send events to EventHub. 
    /// Any user parameter that sends EventHub events will eventually get bound to this object. 
    /// This will queue events and send in batches, also keeping under the 256kb event hub limit per batch. 
    /// </summary>
    public class EventHubAsyncCollector : IAsyncCollector<EventData>
    {
        private readonly EventHubClient _client;

        private List<EventData> _list = new List<EventData>();

        // total size of bytes in _list that we'll be sending in this batch. 
        private int _currentByteSize = 0;

        private const int BatchSize = 100;

        // Suggested to use 240k instead of 256k to leave padding room for headers.
        private const int MaxByteSize = 240 * 1000; 

        /// <summary>
        /// Create a sender around the given client. 
        /// </summary>
        /// <param name="client"></param>
        public EventHubAsyncCollector(EventHubClient client)
        {
            _client = client;
        }

        /// <summary>
        /// Add an event. 
        /// </summary>
        /// <param name="item">The event to add</param>
        /// <param name="cancellationToken">a cancellation token. </param>
        /// <returns></returns>
        public async Task AddAsync(EventData item, CancellationToken cancellationToken = default(CancellationToken))
        {
            while (true)
            {
                lock (_list)
                {
                    var size = (int)item.SerializedSizeInBytes;

                    if (size > MaxByteSize)
                    {
                        // Single event is too large to add.
                        string msg = string.Format("Event is too large. Event is approximately {0}b and max size is {0}b", size, MaxByteSize);
                        throw new InvalidOperationException(msg);
                    }

                    if ((_currentByteSize + size > MaxByteSize) || (_list.Count >= BatchSize))
                    {
                        // We should flush. 
                        // Release the lock, flush, and then loop around and try again. 
                    }
                    else
                    {
                        _list.Add(item);
                        _currentByteSize += size;
                        return;
                    }
                }

                await this.FlushAsync(cancellationToken);                
            }
        }

        /// <summary>
        /// synchronously flush events that have been queued up via AddAsync.
        /// </summary>
        /// <param name="cancellationToken">a cancellation token</param>
        /// <returns></returns>
        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            EventData[] batch = null;
            lock (_list)
            {
                batch = _list.ToArray();
                _list.Clear();
                _currentByteSize = 0;
            }

            if (batch.Length > 0)
            {
                await _client.SendBatchAsync(batch);

                // Dispose all messages to help with memory pressure. If this is missed, the finalizer thread will still get them. 
                foreach (var msg in batch)
                {
                    msg.Dispose();
                }
            }
        }
    }
}