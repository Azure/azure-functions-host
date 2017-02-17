// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Microsoft.WebJobs.Script.Tests
{
    internal sealed class SingleThreadSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<Tuple<SendOrPostCallback, object>> _workItems =
            new ConcurrentQueue<Tuple<SendOrPostCallback, object>>();
        private readonly bool _runOnEmptyQueue;
        private bool _stopped;

        public SingleThreadSynchronizationContext(bool runOnEmptyQueue = false)
        {
            _runOnEmptyQueue = runOnEmptyQueue;
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            _workItems.Enqueue(new Tuple<SendOrPostCallback, object>(d, state));
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            throw new NotSupportedException();
        }

        public void Stop()
        {
            _stopped = true;
        }

        public void Run()
        {
            Tuple<SendOrPostCallback, object> item;
            while (!_stopped && (_workItems.TryDequeue(out item) || _runOnEmptyQueue))
            {
                if (item != null)
                {
                    item.Item1(item.Item2);
                }
            }
        }
    }
}
