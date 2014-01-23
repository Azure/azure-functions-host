using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;

namespace AzureTables
{
    internal class AzureTable : IAzureTable, ISelfWatch
    {
        private Stopwatch _timeWrite = new Stopwatch();
        private Stopwatch _timeRead = new Stopwatch();

        private readonly TableCore _core;

        // Writes must be batched by partition key. This means if the caller hits us with different partition keys, they'll keep forcing flushes.
        // Do some batching to protect against that. 
        // key value is the partition key. 
        private Dictionary<string, WriterState> _writerMap = new Dictionary<string, WriterState>();

        internal AzureTable(TableCore core)
        {
            _core = core;
        }

        public static AzureTable NewInMemory()
        {
            return new AzureTable(new LocalTableCore());
        }

        public AzureTable(CloudStorageAccount account, string tableName)
        {
            TableClient.ValidateAzureTableName(tableName);
            _core = new LiveTableCore(account, tableName);
        }

        public AzureTable<TPartRowKey, TValue> GetTypeSafeWrapper<TPartRowKey, TValue>(Func<TPartRowKey, Tuple<string, string>> funcGetRowPartKey) where TValue : new()
        {
            // $$$ Consistency issues with flushing?
            return new AzureTable<TPartRowKey, TValue>(_core, funcGetRowPartKey);
        }

        public AzureTable<TValue> GetTypeSafeWrapper<TValue>() where TValue : new()
        {
            // $$$ Consistency issues with flushing?
            return new AzureTable<TValue>(_core);
        }

        // Flush all outstanding write operations. 
        public void Flush()
        {
            if (_writerMap.Count > 0)
            {
                _timeWrite.Start();
                foreach (var kv in _writerMap)
                {
                    kv.Value.FlushAsync();
                }
                _writerMap.Clear();

                _timeWrite.Stop();
            }
        }


        // Need co create more cache space. 
        private void FlushPartial()
        {
            Flush();
        }

        // Delete the entire table
        public void Clear()
        {
            Flush(); // may end up deleting things we just wrote to. 

            _core.DeleteTable();
        }

        public void ClearAsync()
        {
            Flush(); // may end up deleting things we just wrote to. 

            _core.DeleteTableAsync();
        }

        public void Delete(string partitionKey, string rowKey = null)
        {
            Flush(); // may end up deleting things we just wrote to. 

            if (rowKey == null)
            {
                _core.DeleteTablePartition(partitionKey);
            }
            else
            {
                _core.DeleteTableRow(partitionKey, rowKey);
            }
        }

        public IEnumerable<IDictionary<string, string>> Enumerate(string partitionKey = null)
        {
            Flush();

            IEnumerable<GenericEntity> results;
            try
            {
                _timeRead.Start();
                _countRowsRead++;

                results = _core.Enumerate(partitionKey);

            }
            finally
            {
                _timeRead.Stop();
            }

            if (results == null)
            {
                return null;
            }

            // Beware, tables can be huge, so return a deferred query
            IEnumerable<IDictionary<string, string>> list = from item in results
                                                            select Normalize(item);


            list = new WrapperEnumerable<IDictionary<string, string>>(list)
            {
                OnBefore = () => _timeRead.Start(),
                OnAfter = () => _timeRead.Stop()
            };
            return list;
        }

        private static IDictionary<string, string> Normalize(GenericEntity item)
        {
            item.properties["PartitionKey"] = item.PartitionKey;
            item.properties["RowKey"] = item.RowKey;
            return item.properties;
        }

        public IDictionary<string, string> Lookup(string partitionKey, string rowKey)
        {
            Flush();

            try
            {
                _timeRead.Start();
                _countRowsRead++;

                GenericEntity all = _core.Lookup(partitionKey, rowKey);

                if (all == null)
                {
                    return null;
                }

                return Normalize(all);
            }
            finally
            {
                _timeRead.Stop();
            }
        }

        public void Write(string partitionKey, string rowKey, object values)
        {
            _countRowsWritten++;

            IDictionary<string, string> dict = values as IDictionary<string, string>;
            if (dict == null)
            {
                dict = ObjectBinderHelpers.ConvertObjectToDict(values);
            }

            WriterState writer;

            if (!_writerMap.TryGetValue(partitionKey, out writer))
            {
                if (_writerMap.Count > PartitionKeyCacheSize)
                {
                    // Keep cache size limited
                    Flush();
                }

                writer = new WriterState(_core);
                _writerMap.Add(partitionKey, writer);
            }

            try
            {
                _timeWrite.Start();
                writer.WriteAsync(dict, partitionKey, rowKey); // Add to this batch, flush this batch when full. 
            }
            finally
            {
                _timeWrite.Stop();
            }
        }

        // Each batch must have the same partition key. 
        // The biggest perf killer for table upload is small batching, which can happen if the partition keys are heavily varried.
        // So we cache and group entries together by partition key
        // The larger the partition, the longer we may be blocked during a Flush().
        const int PartitionKeyCacheSize = 10000;

        private volatile int _countRowsWritten = 0;
        private volatile int _countRowsRead = 0;

        public string GetStatus()
        {
            int read = _countRowsRead;
            int write = _countRowsWritten;

            StringBuilder sb = new StringBuilder();
            if (read > 0)
            {
                sb.AppendFormat("Read {0} rows. ({1} time) ", read, _timeRead.Elapsed);
            }
            if (write > 0)
            {
                sb.AppendFormat("Wrote {0} rows. ({1} time)", write, _timeWrite.Elapsed);
            }
            if (sb.Length == 0)
            {
                return "No table activity.";
            }
            return sb.ToString();
        }

        // Does batching of writes on a single TableServiceContext.
        // The biggest performance issue for writing tables is batching. Batches must all have the same partition key.
        // Batching can make a 100x perf difference.
        // Also good to hit from multiple threads and even multiple nodes.
        class WriterState
        {
            private TableCore _core;

            public WriterState(TableCore core)
            {
                _core = core;
            }

            // Writer state

            private int _rowCounter = 0;
            private int _batchSize = 0;

            private ITableCorePartitionWriter _coreCtx;

            private string _lastPartitionKey;
            private HashSet<Tuple<string, string>> _dups = new HashSet<Tuple<string, string>>();

            public int BatchSize { get { return _batchSize; } }

            // Row, Partition key can't have \,/,#,?. Must be < 1024 bytes. 
            // Azure gives cryptic errors, so validate this now. 
            private static void ValidateSystemProperty(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException("Table key can not be empty");
                }

                if (value.Length >= 512)
                {
                    throw new InvalidOperationException("key is too long");
                }

                if (Regex.IsMatch(value, @"[\\\/#\?]"))
                {
                    throw new InvalidOperationException(string.Format("Illegal character in key:" + value));
                }
            }

            public void WriteAsync(IDictionary<string, string> values, string partitionKey, string rowKey)
            {
                ValidateSystemProperty(partitionKey);
                ValidateSystemProperty(rowKey);

                var entity = new GenericEntity { RowKey = rowKey, PartitionKey = partitionKey, properties = values };
                _rowCounter++;

                // All rows in the batch must have the same partition key.
                // If we changed partition key, then flush the batch.
                if ((_lastPartitionKey != null) && (_lastPartitionKey != entity.PartitionKey))
                {
                    FlushAsync();
                }

                if (_coreCtx == null)
                {
                    _dups.Clear();
                    _lastPartitionKey = null;
                    _coreCtx = _core.NewPartitionWriter(partitionKey);
                    _batchSize = 0;
                }

                var key = Tuple.Create(entity.PartitionKey, entity.RowKey);
                bool dupWithinBatch = _dups.Contains(key);
                _dups.Add(key);

                // Upsert allows overwriting existing keys. But still must be unique within a batch.
                if (!dupWithinBatch)
                {
                    _coreCtx.AddObject(entity);
                }

                _lastPartitionKey = entity.PartitionKey;
                _batchSize++;

                if (_batchSize % UploadBatchSize == 0)
                {
                    // Beware, if keys collide within a batch, we get a very cryptic error and 400.
                    // If they collide across batches, we get a more useful 409 (conflict). 
                    try
                    {
                        FlushAsync();
                    }
                    catch (DataServiceRequestException de)
                    {
                        var e = de.InnerException as DataServiceClientException;
                        if (e != null)
                        {
                            if (e.StatusCode == 409)
                            {
                                // Conflict. Duplicate keys. We don't get the specific duplicate key.
                                // Server shouldn't do this if we support upsert.
                                // (although an old emulator that doesn't yet support upsert may throw it).
                                throw new InvalidOperationException(string.Format("Table has duplicate keys. {0}", e.Message));
                            }
                        }
                        throw de; // rethrow
                    }
                }
            }

            public void FlushAsync()
            {
                if (_coreCtx != null)
                {
                    _coreCtx.Flush();
                    _coreCtx = null;
                }
            }

            // Batches must be < 100. 
            // but all rows in the batch must have the same partition key
            // Larger batches are more efficient. 
            private const int UploadBatchSize = 90;
        }
    }
}
