// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Log whether this container has any active functions. 

    public interface IActiveCounter
    {
        void Increment();
        void Decrement();
    }

    public class ContainerActiveLogger : IActiveCounter
    {
        private static TimeSpan _interval = TimeSpan.FromSeconds(5);

        private int _outstandingCount;
        private readonly CloudTable _instanceTable;
        private readonly string _containerName;

        private bool _recent; // For catching quick functions

        private CancellationTokenSource _cancel = null;
        private Task _scanner;

        public ContainerActiveLogger(string containerName, CloudTable instanceTable)
        {
            this._instanceTable = instanceTable;
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

        public void Increment()
        {
            lock (_lock)
            {
                if (_cancel == null)
                {
                    _cancel = new CancellationTokenSource();
                    _scanner = PollerAsync();
                }

                _recent = true;

                int count = Interlocked.Increment(ref _outstandingCount);
            }
        }
        public void Decrement()
        {
            int count = Interlocked.Decrement(ref _outstandingCount);
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

                bool active = (_recent || _outstandingCount > 0);
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
            return ContainerActiveEntity.LookupAsync(_instanceTable, timeBucket, _containerName);
        }

        private Task SaveAsync(ContainerActiveEntity prevEntry)
        {
            TableOperation insertOperation = TableOperation.InsertOrReplace(prevEntry);

            // Execute the insert operation.
            return _instanceTable.ExecuteAsync(insertOperation);
        }
    }
}