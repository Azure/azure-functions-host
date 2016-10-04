// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Log whether the container is active or not.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    internal class ContainerActiveLogger
    {
        // MAximum length of an ActivationEvent. This aides in querying backwards. 
        private const int LengthThreshold = 50;

        private static TimeSpan _interval = TimeSpan.FromSeconds(5);

        // Track functionInstanceGuids (instead of just a single integer counter) in case we missed an event or double reported an event. 
        private HashSet<Guid> _outstandingCount = new HashSet<Guid>();
        private readonly ILogTableProvider _tableLookup;
        private readonly string _containerName;

        private bool _recent; // For catching quick functions

        private CancellationTokenSource _cancel = null;
        private Task _scanner;

        public ContainerActiveLogger(string containerName, ILogTableProvider tableLookup)
        {
            this._tableLookup = tableLookup;
            this._containerName = containerName;
        }

        object _lock = new object();

        public Task StopAsync()
        {
            if (_cancel != null)
            {
                _cancel.Cancel();
                return _scanner;
            }
            else {
                // already stopped
                return Task.FromResult(0);
            }
        }

        public void Increment(Guid instanceId)
        {
            lock (_lock)
            {
                if (_cancel == null)
                {
                    _cancel = new CancellationTokenSource();
                    _scanner = PollerAsync();
                }

                _recent = true;

                _outstandingCount.Add(instanceId);
            }
        }
        public void Decrement(Guid instanceId)
        {
            lock(_lock)
            {
                _outstandingCount.Remove(instanceId);
            }
        }

        // this should be the only thread writing to this container
        // Write a "active" entry every interval. 
        // If a previous entry exists, then extend its duration (rather that writing many entries). That simplifies the reader. 
        async Task PollerAsync()
        {
            do
            {
                try
                {
                    await Task.Delay(_interval, _cancel.Token);
                }
                catch (OperationCanceledException)
                {
                    // Don't return yet. One last chance to flush 
                }

                bool hasOutstanding;
                lock (_lock)
                {
                    hasOutstanding = _outstandingCount.Count > 0;
                }

                bool active = _recent || hasOutstanding;
                _recent = false;

                if (active)
                {
                    var now = DateTime.UtcNow;

                    // If previous exists, update it
                    var currentBucket = TimeBucket.ConvertToBucket(now);
                    var prevBucket = currentBucket - 1;

                    ContainerActiveEntity prevEntry = await TryGetAsync(prevBucket);
                    if (prevEntry == null)
                    {
                        prevEntry = await TryGetAsync(currentBucket);
                    }
                    if (prevEntry != null)
                    {
                        if (prevEntry.GetLength() > LengthThreshold)
                        {
                            prevEntry = null;
                        }
                    }

                    if (prevEntry == null)
                    {
                        prevEntry = ContainerActiveEntity.New(now, _containerName);
                    }

                    // Update the length on the previous entry
                    prevEntry.EndTime = now;
                    await SaveAsync(prevEntry);
                }
            } while (!_cancel.IsCancellationRequested);
        }

        private Task<ContainerActiveEntity> TryGetAsync(long timeBucket)
        {
            var instanceTable = _tableLookup.GetTableForTimeBucket(timeBucket);
            return ContainerActiveEntity.LookupAsync(instanceTable, timeBucket, _containerName);
        }

        private Task SaveAsync(ContainerActiveEntity prevEntry)
        {
            TableOperation insertOperation = TableOperation.InsertOrReplace(prevEntry);
            
            var instanceTable = _tableLookup.GetTableForDateTime(prevEntry.StartTime);

            // Execute the insert operation.
            return instanceTable.SafeExecuteAsync(insertOperation);
        }
    }
}