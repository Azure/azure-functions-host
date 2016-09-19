// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.


using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging.Internal;
using Microsoft.WindowsAzure.Storage.Table;

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
        EntityCollection<InstanceTableEntity> _instances = new EntityCollection<InstanceTableEntity>();
        EntityCollection<RecentPerFuncEntity> _recents = new EntityCollection<RecentPerFuncEntity>();
        EntityCollection<FunctionDefinitionEntity> _funcDefs = new EntityCollection<FunctionDefinitionEntity>();

        EntityCollection<TimelineAggregateEntity> _timespan = new EntityCollection<TimelineAggregateEntity>();

        // Container is common shared across all log writer instances 
        static ContainerActiveLogger _container;
        CloudTableInstanceCountLogger _instanceLogger;

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

        // Get the "size" of this execution unit.
        private static int GetContainerSize()
        {
            string raw = Environment.GetEnvironmentVariable("WEBSITE_MEMORY_LIMIT_MB");
            int size;
            if (int.TryParse(raw, out size))
            {
                return size;
            }
            return 1;
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
                    if (_instanceLogger == null)
                    {
                        int size = GetContainerSize();
                        _instanceLogger = new CloudTableInstanceCountLogger(_containerName, _instanceTable, size);
                    }
                }
                if (item.IsCompleted())
                {
                    _container.Decrement(item.FunctionInstanceId);
                    _instanceLogger.Decrement(item.FunctionInstanceId);
                }
                else
                {
                    _container.Increment(item.FunctionInstanceId);
                    _instanceLogger.Increment(item.FunctionInstanceId);
                }
            }

            lock (_lock)
            {
                if (_seenFunctions.Add(item.FunctionName))
                {
                    _funcDefs.Add(FunctionDefinitionEntity.New(item.FunctionName));
                }
            }
                   
            // Both Start and Completed log here. Completed will overwrite a Start entry. 
            lock (_lock)
            {
                _instances.Add(InstanceTableEntity.New(item));
                _recents.Add(RecentPerFuncEntity.New(_containerName, item));
            }

            if (item.IsCompleted())
            {
                // For completed items, aggregate total passed and failed within a time bucket. 
                // Time aggregate is flushed later. 
                // Don't flush until we've moved onto the next interval. 
                {
                    var newEntity = TimelineAggregateEntity.New(_containerName, item.FunctionName, item.StartTime, _uniqueId);
                    lock (_lock)
                    {
                        // If we already have an entity at this time slot (specified by rowkey), then use that so that 
                        // we update the existing counters. 
                        var existingEntity = _timespan.GetFromRowKey(newEntity.RowKey);
                        if (existingEntity == null)
                        {
                            _timespan.Add(newEntity);
                            existingEntity = newEntity;
                        }

                        Increment(item, existingEntity);
                    }
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
                foreach (var entity in _timespan)
                {
                    long thisBucket = TimeBucket.ConvertToBucket(entity.Timestamp.DateTime);
                    if ((thisBucket < currentBucket) || always)
                    {
                        flush.Add(entity);
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

        private async Task FlushCoreAsync()
        {
            await FlushTimelineAggregateAsync(true);
            await FlushIntancesAsync(true);

            if (_container != null)
            {
                await _container.StopAsync();
            }

            if (_instanceLogger != null)
            {
                await _instanceLogger.StopAsync();
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
                    Task tUpload = _instanceTable.SafeWriteAsync(batch);
                    t.Add(tUpload);
                    batch = new TableBatchOperation();
                }
            }
            if (batch.Count > 0)
            {
                Task tUpload = _instanceTable.SafeWriteAsync(batch);
                t.Add(tUpload);
            }


            await Task.WhenAll(t);
        }

        // Collection where adding in the same RowKey replaces a previous entry with that key. 
        // This is single-threaded. Caller must lock. 
        // All entities in this collection must be within the same partition. 
        private class EntityCollection<T>  : IEnumerable<T> where T : TableEntity 
        {
            // Ordering doesn't matter since azure tables will order them for us. 
            private Dictionary<string, T> _map = new Dictionary<string, T>();

            public void Add(T entry)
            {                
                string row = entry.RowKey;
                _map[row] = entry;                
            }

            public int Count
            {
                get { return _map.Count; }
            }

            public T[] ToArray()
            {
                return _map.Values.ToArray();
            }

            public void Clear()
            {
                _map.Clear();
            }

            // Get existing entity at this rowkey, or return null.
            public T GetFromRowKey(string rowKey)
            {
                T entity;
                _map.TryGetValue(rowKey, out entity);
                return entity;
            }

            internal void Remove(string rowKey)
            {
                _map.Remove(rowKey);
            }

            public IEnumerator<T> GetEnumerator()
            {
                return _map.Values.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return _map.Values.GetEnumerator();
            }
        }
    }    
}
