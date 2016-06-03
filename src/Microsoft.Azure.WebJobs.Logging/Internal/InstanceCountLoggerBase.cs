// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Logging.Internal
{
    /// <summary>
    /// Base class for aggregating Increment/Decrement counters. 
    /// Maintains a background poller thread that periodically logs a count. 
    /// </summary>
    public abstract class InstanceCountLoggerBase
    {
        // Track functionInstanceGuids (instead of just a single integer counter) in case we missed an event or double reported an event. 
        private readonly HashSet<Guid> _outstandingCount = new HashSet<Guid>();
                
        // Capture the maximum value of _outstandingCount.Count per each interval. 
        // If we get 1000 functions that each take 1 ms,  a random sample may catch _outstandingCount.Count at 0 and miss the activity. 
        private int _maxCount;

        // Total number of unique increments. 
        private int _totalCount;

        private Worker _worker;
        
        object _lock = new object();

        /// <summary>
        /// Stop logging. Flush outstanding entries.
        /// </summary>
        /// <returns></returns>
        public Task StopAsync()
        {
            lock(_lock)
            {
                if (_worker != null)
                {
                    var task = _worker.StopAsync();
                    _worker = null;
                    return task;
                }
                else {
                    // already stopped
                    return Task.FromResult(0);
                }
            }
        }

        /// <summary>
        /// Begin logging a function instance guid. 
        /// </summary>
        /// <param name="instanceId">instance guid for function that started</param>
        public void Increment(Guid instanceId)
        {
            lock (_lock)
            {
                if (_worker == null)
                {
                    _worker = Worker.Start(this);                    
                }

                if (_outstandingCount.Add(instanceId))
                {
                    _totalCount++;
                }

                _maxCount = Math.Max(_maxCount, _outstandingCount.Count);
            }
        }

        /// <summary>
        /// Stop logging a function instance guid. Guid should have been previously passed to Increment().
        /// </summary>
        /// <param name="instanceId">instance guid for function that completed.</param>
        public void Decrement(Guid instanceId)
        {
            lock (_lock)
            {
                _outstandingCount.Remove(instanceId);
            }
        }

        /// <summary>
        /// Poll, return the ticks. 
        /// </summary>
        /// <param name="token">cancellation token to interupt the poll. 
        /// Don't throw when cancelled, just return early because we still need a tick counter returned.</param>
        /// <returns>Tick counter after the poll. </returns>
        protected abstract Task<long> WaitOnPoll(CancellationToken token);        

        /// <summary>
        /// Called to write 
        /// </summary>
        /// <param name="ticks">time ticks returned via WaitOnPoll</param>
        /// <param name="currentActive">number of outstanding Increment calls. </param>
        /// <param name="totalThisPeriod">total number of unique Increment calls this period.</param>
        /// <returns></returns>
        protected abstract Task WriteEntry(long ticks, int currentActive, int totalThisPeriod);

        // Wrap cancellation token and task in a separate class so that StopAsync()  doesn't reccyle them. 
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
        class Worker
        {
            private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
            private Task _pollTask; 
            private InstanceCountLoggerBase _parent;

            public static Worker Start(InstanceCountLoggerBase parent)
            {
                var worker = new Worker();
                worker._parent = parent;
                worker._pollTask = worker.PollerAsync();
                return worker;
            }

            public Task StopAsync()
            {
                _cancel.Cancel();
                return _pollTask;
            }

            async Task PollerAsync()
            {
                do
                {
                    long ticks = await _parent.WaitOnPoll(_cancel.Token);

                    int totalActiveFuncs;
                    int totalThisPeriod;
                    lock (_parent._lock)
                    {
                        totalActiveFuncs = _parent._maxCount;
                        _parent._maxCount = _parent._outstandingCount.Count; // reset. 

                        totalThisPeriod = _parent._totalCount;
                        _parent._totalCount = 0;
                    }

                    try
                    {
                        await _parent.WriteEntry(ticks, totalActiveFuncs, totalThisPeriod);
                    }
                    catch
                    {
                        // Failures from writing (such as IO), may mean missing data, but shouldn't bring down the whole polling. 
                    }
                }
                while (!_cancel.IsCancellationRequested);
            }
        }
    }
}