// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class FakeQueueListener : IListener
    {
        private ITriggeredFunctionExecutor _executor;
        private FakeQueueClient _client;
        private bool _singleDispatch;

        public FakeQueueListener(ITriggeredFunctionExecutor executor, FakeQueueClient client, bool singleDispatch)
        {
            this._executor = executor;
            this._singleDispatch = singleDispatch;
            this._client = client;
        }

        void IListener.Cancel()
        {
            // nop
        }

        void IDisposable.Dispose()
        {
            // nop
        }

        async Task IListener.StartAsync(CancellationToken cancellationToken)
        {
            var items = _client._items.ToArray();

            if (_singleDispatch)
            {
                foreach (var item in items)
                {
                    await _executor.TryExecuteAsync(new TriggeredFunctionData
                    {
                        TriggerValue = new FakeQueueDataBatch { Events = new FakeQueueData[] { item } }
                    }, CancellationToken.None);
                }
            }
            else {
                await _executor.TryExecuteAsync(new TriggeredFunctionData
                {
                    TriggerValue = new FakeQueueDataBatch { Events = items }
                }, CancellationToken.None);
            }
        }

        Task IListener.StopAsync(CancellationToken cancellationToken)
        {
            // nop
            return Task.FromResult(0);
        }
    }
}