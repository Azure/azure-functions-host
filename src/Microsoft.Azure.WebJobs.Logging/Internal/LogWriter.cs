// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Fast logger. 
    // Exposes a single AddAsync() to log one item, and then this will batch them up and write tables in bulk. 
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    internal class LogWriter : ILogWriter
    {
        // Logs from AddAsync() are batched up. They can be explicitly flushed via FlushAsync() and 
        // they get autotmatically flushed at Interval. 
        // Calling AddAsync() will startup the background flusher. Calling FlushAsync() explicitly will disable it. 
        private static TimeSpan _flushInterval = TimeSpan.FromSeconds(45);
        private CancellationTokenSource _cancelBackgroundFlusher = null;
        private Task _backgroundFlusherTask = null;

        // All writing goes to 1 table. 
        private readonly CloudTable _instanceTable;
        private readonly string _containerName; // compute container (not Blob Container) that we're logging for. 

        private string _uniqueId = Guid.NewGuid().ToString();

        // If there's a new function, then write it's definition. 
        HashSet<string> _seenFunctions = new HashSet<string>();

        object _lock = new object();

        // Track for batching. 
        List<InstanceTableEntity> _instances = new List<InstanceTableEntity>();
        List<RecentPerFuncEntity> _recents = new List<RecentPerFuncEntity>();
        List<FunctionDefinitionEntity> _funcDefs = new List<FunctionDefinitionEntity>();

        Dictionary<string, TimelineAggregateEntity> _timespan = new Dictionary<string, TimelineAggregateEntity>();

        // Container is common shared across all log writer instances 
        static ContainerActiveLogger _container;

        public LogWriter(string computerContainerName, CloudTable table)
        {
            if (computerContainerName == null)
            {
                throw new ArgumentNullException("computerContainerName");
            }
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }
            this._containerName = computerContainerName;         
            table.CreateIfNotExists();
            this._instanceTable = table;
        }

        // Background flusher. 
        // Adds() are batched up.  So flush them automatically every interval. 
        // Keeps looping until somebody explicitly calls Flush().
        // Its possible there could be multiple flushers running concurrently. 
        private async Task BackgroundFlushWorkerAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                try
                {
                    await Task.Delay(_flushInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Don't return yet. One last chance to flush 
                    return;
                }

                await this.FlushCoreAsync();
            }
        }

        // Call this under the lock 
        private void StartBackgroundFlusher()
        {
            // Start background object. Do this under a lock to ensure only 1 gets started.             
            if (_backgroundFlusherTask == null)
            {
                _cancelBackgroundFlusher = new CancellationTokenSource();
                _backgroundFlusherTask = BackgroundFlushWorkerAsync(_cancelBackgroundFlusher.Token);
            }
        }

        private async Task StopBackgroundFlusher()
        {
            Task task = null;
            lock(_lock)
            {
                if (_backgroundFlusherTask != null)
                {
                    // Clear the flag before waiting, since the background flusher may call back into Flush()
                    task = _backgroundFlusherTask;
                    _backgroundFlusherTask = null;
                    _cancelBackgroundFlusher.Cancel();                    
                }
            }
            if (task != null)
            {
                await task; // don't wait under a lock. 
            }
        }

        public async Task AddAsync(FunctionInstanceLogItem item, CancellationToken cancellationToken = default(CancellationToken))
        {
            item.Validate();

            {
                lock(_lock)
                {
                    StartBackgroundFlusher();
                    if (_container == null)
                    {
                        _container = new ContainerActiveLogger(_containerName, _instanceTable);
                    }
                }
                if (item.IsCompleted())
                {
                    _container.Decrement(item.FunctionInstanceId);
                }
                else
                {
                    _container.Increment(item.FunctionInstanceId);
                }
            }

            lock (_lock)
            {
                if (_seenFunctions.Add(item.FunctionName))
                {
                    _funcDefs.Add(FunctionDefinitionEntity.New(item.FunctionName));
                }
            }

            if (!item.IsCompleted())
            {
                return;
            }

            lock (_lock)
            {
                _instances.Add(InstanceTableEntity.New(item));
                _recents.Add(RecentPerFuncEntity.New(_containerName, item));
            }

            // Time aggregate is flushed later. 
            // Don't flush until we've moved onto the next interval. 
            {
                var rowKey = TimelineAggregateEntity.RowKeyTimeInterval(item.FunctionName, item.StartTime, _uniqueId);

                lock(_lock)
                {
                    TimelineAggregateEntity x;
                    if (!_timespan.TryGetValue(rowKey, out x))
                    {
                        // Can we flush the old counters?
                        x = TimelineAggregateEntity.New(_containerName, item.FunctionName, item.StartTime, _uniqueId);
                        _timespan[rowKey] = x;
                    }
                    Increment(item, x);
                }
            }

            // Flush every 100 items, maximize with tables. 
            Task t1 = FlushIntancesAsync(false);
            Task t2 = FlushTimelineAggregateAsync();
            await Task.WhenAll(t1, t2);
        }

        // Could flush on a timer. 
        private async Task FlushTimelineAggregateAsync(bool always = false)
        {
            long currentBucket = TimeBucket.ConvertToBucket(DateTime.UtcNow);
            List<TimelineAggregateEntity> flush = new List<TimelineAggregateEntity>();

            lock (_lock)
            {
                foreach (var kv in _timespan)
                {
                    long thisBucket = TimeBucket.ConvertToBucket(kv.Value.Timestamp.DateTime);
                    if ((thisBucket < currentBucket) || always)
                    {
                        flush.Add(kv.Value);
                    }
                }

                foreach (var val in flush)
                {
                    _timespan.Remove(val.RowKey);
                }
            }

            if (flush.Count > 0)
            {
                await WriteBatchAsync(flush);
            }
        }

        // Could flush on a timer. 
        private async Task FlushIntancesAsync(bool always)
        {
            InstanceTableEntity[] x1;
            RecentPerFuncEntity[] x2;
            FunctionDefinitionEntity[] x3;

            lock (_lock)
            {
                if (!always)
                {
                    if (_instances.Count < 90)
                    {
                        return;
                    } 
                }

                x1 = _instances.ToArray();
                x2 = _recents.ToArray();
                x3 = _funcDefs.ToArray();
                _instances.Clear();
                _recents.Clear();
                _funcDefs.Clear();
            }
            Task t1 = WriteBatchAsync(x1);
            Task t2 = WriteBatchAsync(x2);
            Task t3 = WriteBatchAsync(x3);
            await Task.WhenAll(t1, t2, t3);
        }

        private async Task FlushCoreAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await FlushTimelineAggregateAsync(true);
            await FlushIntancesAsync(true);

            if (_container != null)
            {
                await _container.StopAsync();
            }
        }

        // Flush async can also stop the background flusher. 
        public async Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await StopBackgroundFlusher();

            await FlushCoreAsync();
        }

        private static void Increment(FunctionInstanceLogItem item, TimelineAggregateEntity x)
        {
            x.TotalRun++;

            if (item.IsSucceeded())
            {
                x.TotalPass++;
            }
            else
            {
                x.TotalFail++;
            }
        }

        // Limit of 100 per batch. 
        // Parallel uploads. 
        private async Task WriteBatchAsync<T>(IEnumerable<T> e1) where T : TableEntity
        {            
            HashSet<string> rowKeys = new HashSet<string>();

            int batchSize = 90;

            TableBatchOperation batch = new TableBatchOperation();

            List<Task> t = new List<Task>();

            foreach (var e in e1)
            {
                if (!rowKeys.Add(e.RowKey))
                {
                    // Already present
                }

                batch.InsertOrReplace(e);
                if (batch.Count >= batchSize)
                {
                    Task tUpload = _instanceTable.ExecuteBatchAsync(batch);
                    t.Add(tUpload);
                    batch = new TableBatchOperation();
                }
            }
            if (batch.Count > 0)
            {
                Task tUpload = _instanceTable.ExecuteBatchAsync(batch);
                t.Add(tUpload);
            }


            await Task.WhenAll(t);
        }    
    }    
}
