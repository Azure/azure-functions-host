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
    public class LogWriter
    {
        // All writing goes to 1 table. 
        private readonly CloudTable _instanceTable;
        private readonly string _containerName; // compute container that we're logging to. 

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

        public LogWriter(string containerName, CloudTable table)
        {
            if (containerName == null)
            {
                throw new ArgumentNullException("containerName");
            }
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }
            this._containerName = containerName;         
            table.CreateIfNotExists();
            this._instanceTable = table;
        }

        public async Task AddAsync(FunctionLogItem item, CancellationToken cancellationToken = default(CancellationToken))
        {
            {
                lock(_lock)
                {
                    if (_container == null)
                    {
                        _container = new ContainerActiveLogger(_containerName, _instanceTable);
                    }
                }
                if (item.IsCompleted())
                {
                    _container.Decrement();
                }
                else {
                    _container.Increment();
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
            //Stopwatch sw = Stopwatch.StartNew();
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

        public async Task FlushAsync()
        {
            await FlushTimelineAggregateAsync(true);
            await FlushIntancesAsync(true);

            await _container.StopAsync();
        }

        private static void Increment(FunctionLogItem item, IAggregate x)
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

        private Task WriteBatchAsync<T>(IEnumerable<T> e1) where T : TableEntity
        {
            return WriteBatch2Async(e1);
        }

        // Limit of 100 per batch. 
        // Parallel uploads. 
        private async Task WriteBatch2Async<T>(IEnumerable<T> e1) where T : TableEntity
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

        private async Task WriteBatchSerialAsync<T>(IEnumerable<T> e1) where T : TableEntity
        {
            HashSet<string> rowKeys = new HashSet<string>();

            int batchSize = 90;

            TableBatchOperation batch = new TableBatchOperation();

            foreach (var e in e1)
            {
                if (!rowKeys.Add(e.RowKey))
                {
                    // Already present
                }

                batch.InsertOrReplace(e);
                if (batch.Count >= batchSize)
                {
                    await _instanceTable.ExecuteBatchAsync(batch);
                    batch = new TableBatchOperation();
                }
            }
            if (batch.Count > 0)
            {
                await _instanceTable.ExecuteBatchAsync(batch);
            }
        }
    }    
}
