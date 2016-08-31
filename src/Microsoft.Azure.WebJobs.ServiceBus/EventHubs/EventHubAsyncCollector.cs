// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // Core object to send events to EventHub. 
    // Any user parameter that sends events will eventually get bound to this object. 
    // This gets wrappers with various adapters and passed to the user function. 
    internal class EventHubAsyncCollector : IAsyncCollector<EventData>
    {
        private readonly EventHubClient _client;

        private List<EventData> _list = new List<EventData>();
        private const int BatchSize = 100;

        public EventHubAsyncCollector(EventHubClient client)
        {
            _client = client;
        }

        public async Task AddAsync(EventData eventData, CancellationToken cancellationToken = default(CancellationToken))
        {
            bool flush;
            lock (_list)
            {
                _list.Add(eventData);
                flush = _list.Count > BatchSize;
            }

            if (flush)
            {
                await this.FlushAsync(cancellationToken);
            }
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            EventData[] batch = null;
            lock (_list)
            {
                batch = _list.ToArray();
                _list.Clear();
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