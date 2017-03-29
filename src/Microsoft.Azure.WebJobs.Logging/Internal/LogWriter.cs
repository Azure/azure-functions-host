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

        // Writes go to multiple tables, sharded by timestamp. 
        private readonly ILogTableProvider _logTableProvider;

        // We have 3 levels of logging. 
        // HostName identifies a homogenous set of compute (such as a scale out).  It's like site name. 
        // MachineName is the name of this machine. 
        // _uniqueId discerns between multiple loggers on the same machine. This ensures multiple writers don't conflict with each other. 
        private readonly string _hostName;
        private readonly string _machineName; // compute container (not Blob Container) that we're logging for. 
        private string _uniqueId = Guid.NewGuid().ToString();

        // If there's a new function, then write it's definition. 
        HashSet<string> _seenFunctions = new HashSet<string>();

        object _lock = new object();

        // Track for batching. 
        EntityCollection<FunctionDefinitionEntity> _funcDefs = new EntityCollection<FunctionDefinitionEntity>();

        EntityCollection<TimelineAggregateEntity> _timespan = new EntityCollection<TimelineAggregateEntity>();

        // Container is common shared across all log writer instances 
        static ContainerActiveLogger _container;
        private CloudTableInstanceCountLogger _instanceLogger;

        private Action<Exception> _onException;

        public LogWriter(string hostName, string machineName, ILogTableProvider logTableProvider, Action<Exception> onException = null)
        {
            if (machineName == null)
            {
                throw new ArgumentNullException("machineName");
            }
            if (logTableProvider == null)
            {
                throw new ArgumentNullException("logTableProvider");
            }
            if (hostName == null)
            {
                throw new ArgumentNullException("hostName");
            }
            this._hostName = hostName;
            this._machineName = machineName;
            this._logTableProvider = logTableProvider;
            this._onException = onException;
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
            lock (_lock)
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

        // Tracks currently executing functions.
        // FunctionInstanceId --> function 
        Dictionary<Guid, FunctionInstanceLogItem> _activeFuncs = new Dictionary<Guid, FunctionInstanceLogItem>();
        HashSet<Guid> _completedFunctions = new HashSet<Guid>();

        public Task AddAsync(FunctionInstanceLogItem item, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }
            item.Validate();
            item.FunctionId = FunctionId.Build(this._hostName, item.FunctionName);

            // Both Start and Completed log here. Completed will overwrite a Start entry. 
            lock (_lock)
            {
                _activeFuncs[item.FunctionInstanceId] = item;
            }

            lock (_lock)
            {
                StartBackgroundFlusher();
                if (_container == null)
                {
                    _container = new ContainerActiveLogger(_machineName, _logTableProvider);
                }
                if (_instanceLogger == null)
                {
                    int size = GetContainerSize();
                    _instanceLogger = new CloudTableInstanceCountLogger(_machineName, _logTableProvider, size);
                }
            }

            if (item.IsCompleted())
            {
                _container.Decrement(item.FunctionInstanceId);
                _instanceLogger.Decrement(item.FunctionInstanceId);

                _completedFunctions.Add(item.FunctionInstanceId);
            }
            else
            {
                _container.Increment(item.FunctionInstanceId);
                _instanceLogger.Increment(item.FunctionInstanceId);
            }

            lock (_lock)
            {
                if (_seenFunctions.Add(item.FunctionName))
                {
                    _funcDefs.Add(FunctionDefinitionEntity.New(item.FunctionId, item.FunctionName));
                }
            }

            if (item.IsCompleted())
            {
                // For completed items, aggregate total passed and failed within a time bucket. 
                // Time aggregate is flushed later. 
                // Don't flush until we've moved onto the next interval. 
                {
                    var newEntity = TimelineAggregateEntity.New(_machineName, item.FunctionId, item.StartTime, _uniqueId);
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

            // Results will get written on a background thread            
            return Task.FromResult(0);
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


        private FunctionInstanceLogItem[] Update()
        {
            FunctionInstanceLogItem[] items;
            lock (_lock)
            {
                items = _activeFuncs.Values.ToArray();
            }
            foreach (var item in items)
            {
                item.Refresh(_flushInterval);
            }
            return items;
        }

        // Could flush on a timer. 
        private async Task FlushIntancesAsync()
        {
            // Before writing, give items a chance to refresh 
            var itemsSnapshot = Update();

            // Write entries
            var instances = Array.ConvertAll(itemsSnapshot, item => InstanceTableEntity.New(item));
            var recentInvokes = Array.ConvertAll(itemsSnapshot, item => RecentPerFuncEntity.New(_machineName, item));

            FunctionDefinitionEntity[] functionDefinitions;

            lock (_lock)
            {
                functionDefinitions = _funcDefs.ToArray();
                _funcDefs.Clear();
            }
            Task t1 = WriteBatchAsync(instances);
            Task t2 = WriteBatchAsync(recentInvokes);
            Task t3 = WriteBatchAsync(functionDefinitions);
            await Task.WhenAll(t1, t2, t3);

            // After we write to table, remove all completed functions. 
            lock (_lock)
            {
                foreach (var completedId in _completedFunctions)
                {
                    _activeFuncs.Remove(completedId);
                }
                _completedFunctions.Clear();
            }
        }

        private async Task FlushCoreAsync()
        {
            try
            {
                await FlushTimelineAggregateAsync(true);
                await FlushIntancesAsync();
            }
            catch (Exception ex)
            {
                // provide a chance for external users to log the exception
                // before we rethrow it
                _onException?.Invoke(ex);

                throw;
            }

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
        private Task WriteBatchAsync<T>(IEnumerable<T> e1) where T : TableEntity, IEntityWithEpoch
        {
            return this._logTableProvider.WriteBatchAsync(e1);
        }

        // Collection where adding in the same RowKey replaces a previous entry with that key. 
        // This is single-threaded. Caller must lock. 
        // All entities in this collection must have unique row keys across the partition and tables.
        private class EntityCollection<T> : IEnumerable<T> where T : TableEntity
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
