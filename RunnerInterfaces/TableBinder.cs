using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Data.Services.Common;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace AzureTables
{
    // When counter  = 0, we have no outstanding workers.
    class SyncCounter
    {
        // $$$ Fix this. There's a race. Orchestrator's table usage is blocking. 
        #if false 
        //private int _counter;
        Semaphore _semaphore;

        int N = 20;

        public SyncCounter()
        {
            
            _semaphore = new Semaphore(N, N);
        }


        // add an Outstanding worker. 
        // Can block. 
        public void Increment()
        {
            _semaphore.WaitOne();
            //Interlocked.Increment(ref _counter);

            // if > threshold, block. 
        }

        // Worker has finished. 
        // Never blocks.
        public void Decrement()
        {
            _semaphore.Release();
            //Interlocked.Decrement(ref _counter);
        }

        // Block until all work is done. 
        public void WaitForZero()
        {
            for (int i = 0; i < N; i++)
            {
                _semaphore.WaitOne();
            }
        }
#endif
    }

    // Does batching of writes on a single TableServiceContext.
    // The biggest performance issue for writing tables is batching. Batches must all have the same partition key.
    // Batching can make a 100x perf difference.
    // Also good to hit from multiple threads and even multiple nodes.
    class WriterState
    {
        private readonly SyncCounter _parent;
        public WriterState(SyncCounter parent)
        {
            _parent = parent;
        }

        // Writer state

        int _rowCounter = 0;
        int _batchSize = 0;
        TableServiceContext _ctx = null;
        string _lastPartitionKey = null;
        HashSet<Tuple<string, string>> _dups = new HashSet<Tuple<string, string>>();

        public int BatchSize { get { return _batchSize; } }
        
        // Row, Partition key can't have \,/,#,?. Must be < 1024 bytes. 
        // Azure gives cryptic errors, so validate this now. 
        private static void ValidateSystemProperty(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("invalid key");
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

        public void WriteAsync(CloudTableClient client, string tableName, IDictionary<string, string> values, string partitionKey, string rowKey)
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

            if (_ctx == null)
            {
                _dups.Clear();
                _lastPartitionKey = null;
                _ctx = client.GetDataServiceContext();
                _ctx.WritingEntity += new EventHandler<ReadingWritingEntityEventArgs>(ctx_WritingEntity);
                _batchSize = 0;
            }

            var key = Tuple.Create(entity.PartitionKey, entity.RowKey);
            bool dupWithinBatch = _dups.Contains(key);
            _dups.Add(key);

            // Upsert allows overwriting existing keys. But still must be unique within a batch.
            if (!dupWithinBatch)
            {
                _ctx.AttachTo(tableName, entity);
                _ctx.UpdateObject(entity);
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
            if (_ctx != null)
            {
                var ctx = _ctx;
                _ctx = null; 

                // Queue to threadpool. This thread now owns the context
                //_parent.Increment();
                
                var opts = SaveChangesOptions.Batch | SaveChangesOptions.ReplaceOnUpdate;
                //var result = ctx.BeginSaveChangesWithRetries(opts, Callback, _parent);               
                var result = ctx.SaveChangesWithRetries(opts);               
            }
        }

        private void Callback(IAsyncResult result)
        {
            var state = result.AsyncState;
            SyncCounter sync = (SyncCounter)state;

            // sync.Decrement();
        }

        // Batches must be < 100. 
        // but all rows in the batch must have the same partition key
        // Larger batches are more efficient. 
        private const int UploadBatchSize = 90;

        private void ctx_WritingEntity(object sender, ReadingWritingEntityEventArgs args)
        {
            GenericEntity entity = args.Entity as GenericEntity;
            if (entity == null)
            {
                return;
            }

            XElement properties = args.Data.Descendants(AzureTable.MetadataNamespace + "properties").First();

            foreach (var kv in entity.properties)
            {
                // framework will handle row + partition keys. 
                string columnName = kv.Key;
                string edmTypeName = "Edm.String";
                string value = kv.Value;
                XElement e = new XElement(AzureTable.DataNamespace + columnName, value);
                e.Add(new XAttribute(AzureTable.MetadataNamespace + "type", edmTypeName));

                properties.Add(e);
            }
        }
    }

    // Typesafe wrappers.
    public class AzureTable<TPartRowKey, TValue> : AzureTable, IAzureTableReader<TValue>  where TValue : new()
    {
        private Func<TPartRowKey, Tuple<string, string>> _funcGetRowPartKey;

        public AzureTable(CloudStorageAccount account, string tableName, Func<TPartRowKey, Tuple<string, string>> funcGetRowPartKey)
            : base(account, tableName)
        {
            _funcGetRowPartKey = funcGetRowPartKey;
        }

        // Helper for when we have a constant partition key 
        public AzureTable(CloudStorageAccount account, string tableName, string constPartKey)
            : this(account, tableName, rowKey => Tuple.Create(constPartKey, rowKey.ToString()))
        {
        }        

        public TValue Lookup(TPartRowKey row)
        {
            var tuple = _funcGetRowPartKey(row);

            IAzureTableReader<TValue> x = this;
            return x.Lookup(tuple.Item1, tuple.Item2);            
        }

        public void Add(TPartRowKey row, TValue value)
        {
            var tuple = _funcGetRowPartKey(row);
            this.Write(tuple.Item1, tuple.Item2, value);
        }

        TValue IAzureTableReader<TValue>.Lookup(string partitionKey, string rowKey)
        {
            IDictionary<string, string> data = this.Lookup(partitionKey, rowKey);
            return ObjectBinderHelpers.ConvertDictToObject<TValue>(data);
        }

        IEnumerable<TValue> IAzureTableReader<TValue>.Enumerate(string partitionKey)
        {
            return from item in this.Enumerate(partitionKey)
                   select ObjectBinderHelpers.ConvertDictToObject<TValue>(item);
        }
    }

    // Wrapper for when we want to read as a strong type.
    // $$$ Should this implement IDictionary<Tuple<string,string>, TValue> ?
    public class AzureTable<TValue> : AzureTable, IAzureTableReader<TValue> where TValue : new()
    {
        public AzureTable(CloudStorageAccount account, string tableName)
            : base(account, tableName)
        {            
        }

        TValue IAzureTableReader<TValue>.Lookup(string partitionKey, string rowKey)
        {
            IDictionary<string, string> data = this.Lookup(partitionKey, rowKey);

            if (data == null)
            {
                return default(TValue);
            }

            data["PartitionKey"] = partitionKey; // include in case T wants to bind against these.
            data["RowKey"] = rowKey; 
            return ObjectBinderHelpers.ConvertDictToObject<TValue>(data);
        }

        IEnumerable<TValue> IAzureTableReader<TValue>.Enumerate(string partitionKey)
        {
            return from item in this.Enumerate(partitionKey)
                   select ObjectBinderHelpers.ConvertDictToObject<TValue>(item);
        }

        // Enumerate, providing the PartRow key as well as the strongly-typed value. 
        // This is a compatible signature with IDictionary
        public IEnumerable<KeyValuePair<Tuple<string, string>, TValue>> EnumerateDict(string partitionKey = null)
        {
            foreach (var dict in this.Enumerate(partitionKey))
            {
                var partRowKey = Tuple.Create(dict["PartitionKey"], dict["RowKey"]);
                var val = ObjectBinderHelpers.ConvertDictToObject<TValue>(dict);
                yield return new KeyValuePair<Tuple<string, string>, TValue>(partRowKey, val);
            }
        }
    }


    public class AzureTable : IAzureTable, ISelfWatch
    {
        private SyncCounter _sync = new SyncCounter();

        private readonly CloudStorageAccount _account;
        private readonly string _tableName;
        private Stopwatch _timeWrite;
        private Stopwatch _timeRead;

        private CloudTableClient _client;

        // Writes must be batched by partition key. This means if the caller hits us with different partition keys, they'll keep forcing flushes.
        // Do some batching to protect against that. 
        // key value is the partition key. 
        private Dictionary<string, WriterState> _writerMap = new Dictionary<string,WriterState>();

        public AzureTable(CloudStorageAccount account, string tableName)
        {
            ValidateAzureTableName(tableName);

            _timeWrite = new Stopwatch();
            _timeRead = new Stopwatch();

            _account = account;
            _tableName = tableName;

            _client = _account.CreateCloudTableClient();
            _client.CreateTableIfNotExist(tableName);
        }

        public AzureTable<TPartRowKey, TValue> GetTypeSafeWrapper<TPartRowKey, TValue>(Func<TPartRowKey, Tuple<string, string>> funcGetRowPartKey) where TValue : new()
        {
            // $$$ Consistency issues with flushing?
            return new AzureTable<TPartRowKey, TValue>(_account, _tableName, funcGetRowPartKey);
        }

        public AzureTable<TValue> GetTypeSafeWrapper<TValue>() where TValue : new()
        {
            // $$$ Consistency issues with flushing?
            return new AzureTable<TValue>(_account, _tableName);
        }

        // Azure table names are very restrictive, so sanity check upfront to give a useful error.
        // http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
        private static void ValidateAzureTableName(string tableName)
        {
            if (!Regex.IsMatch(tableName, "^[A-Za-z][A-Za-z0-9]{2,62}$"))
            {
                throw new InvalidOperationException(string.Format("{0} is not a valid name for an azure table", tableName));
            }
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

                //_sync.WaitForZero();
                _timeWrite.Stop();
            }
        }


        // Need co create more cache space. 
        private void FlushPartial()
        {
            Flush();

            // $$$ Could optimize this by not flushing the tiny batches. 
            #if false
            int len = _writerMap.Count;
            if (len ==  0)
            {
                return;
            }

            var writers = from kv in _writerMap orderby kv.Value.BatchSize select new { Writer = kv.Value, PartitionKey = kv.Key } ;

            // Only flush the largest batches.
            _timeWrite.Start();
            foreach (var writer in writers.Take(3))
            {
                writer.Writer.FlushAsync();
                _writerMap.Remove(writer.PartitionKey);
            }
            _writerMap.Clear();

            //_sync.WaitForZero();
            _timeWrite.Stop();            
#endif
        } 


        // Delete the entire table
        public void Clear()
        {
            Flush(); // may end up deleting things we just wrote to. 

            // This could take minutes.
            Utility.DeleteTable(_account, _tableName);
        }

        public void ClearAsync()
        {
            Flush(); // may end up deleting things we just wrote to. 

            // This could take a while for delete to be finished            
            CloudTableClient tableClient = _account.CreateCloudTableClient();
            tableClient.DeleteTableIfExist(_tableName);
        }

        public void Delete(string partitionKey, string rowKey = null)
        {
            Flush(); // may end up deleting things we just wrote to. 

            if (rowKey == null)
            {
                Utility.DeleteTablePartition(_account, _tableName, partitionKey);
            }
            else
            {
                Utility.DeleteTableRow(_account, _tableName, partitionKey, rowKey);
            }
        }


        public IEnumerable<IDictionary<string, string>> Enumerate(string partitionKey = null)
        {
            Flush();

            try
            {
                _timeRead.Start();
                _countRowsRead++;

                // Azure will special case this lookup pattern for a single entity. 
                // See http://blogs.msdn.com/b/windowsazurestorage/archive/2010/11/06/how-to-get-most-out-of-windows-azure-tables.aspx 
                try
                {
                    TableServiceContext ctx = _client.GetDataServiceContext();
                    ctx.IgnoreMissingProperties = true;
                    ctx.ReadingEntity += OnReadingEntity;

                    IQueryable<GenericEntity> query;
                    if (partitionKey == null)
                    {
                        query = from o in ctx.CreateQuery<GenericEntity>(_tableName)
                                select o;
                    }
                    else
                    {
                        query = from o in ctx.CreateQuery<GenericEntity>(_tableName)
                                where o.PartitionKey == partitionKey
                                select o;
                    }

                    // Careful, must call AsTableServiceQuery() to get more than 1000 rows. 
                    // http://blogs.msdn.com/b/rihamselim/archive/2011/01/06/retrieving-more-the-1000-row-from-windows-azure-storage.aspx
                    CloudTableQuery<GenericEntity> results = query.AsTableServiceQuery();

                    List<IDictionary<string, string>> list = new List<IDictionary<string, string>>();
                    foreach (var item in results)
                    {
                        item.properties["PartitionKey"] = item.PartitionKey;
                        item.properties["RowKey"] = item.RowKey;
                        list.Add(item.properties);
                    }
                    return list;

                }
                catch (DataServiceQueryException)
                {
                    // Not found. 
                    return null;
                }
            }
            finally
            {
                _timeRead.Stop();
            }
        }

        [DebuggerNonUserCode]
        public IDictionary<string, string> Lookup(string partitionKey, string rowKey)
        {
            Flush();

            try
            {
                _timeRead.Start();
                _countRowsRead++;

                // Azure will special case this lookup pattern for a single entity. 
                // See http://blogs.msdn.com/b/windowsazurestorage/archive/2010/11/06/how-to-get-most-out-of-windows-azure-tables.aspx 
                try
                {
                    TableServiceContext ctx = _client.GetDataServiceContext();
                    ctx.IgnoreMissingProperties = true;
                    ctx.ReadingEntity += OnReadingEntity;

                    var x = from o in ctx.CreateQuery<GenericEntity>(_tableName)
                            where o.PartitionKey == partitionKey && o.RowKey == rowKey
                            select o;
                    GenericEntity all = x.First();

                    if (all == null)
                    {
                        return null;
                    }

                    return all.properties;
                }
                catch (DataServiceQueryException)
                {
                    // Not found. 
                    return null;
                }
            }
            finally
            {
                _timeRead.Stop();
            }
        }

        public static XNamespace AtomNamespace = "http://www.w3.org/2005/Atom";
        public static XNamespace DataNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        public static XNamespace MetadataNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";

        // This manually parses the XML that comes back.
        // This function uses code from this blog entry:
        // http://blogs.msdn.com/b/avkashchauhan/archive/2011/03/28/reading-and-saving-table-storage-entities-without-knowing-the-schema-or-updating-tablestorageentity-schema-at-runtime.aspx
        public static void OnReadingEntity(object sender, ReadingWritingEntityEventArgs args)
        {
            GenericEntity entity = args.Entity as GenericEntity;
            if (entity == null)
            {
                return;
            }

            // read each property, type and value in the payload   
            var properties = args.Entity.GetType().GetProperties();
            var q = from p in args.Data.Element(AtomNamespace + "content")
                                    .Element(MetadataNamespace + "properties")
                                    .Elements()
                    where properties.All(pp => pp.Name != p.Name.LocalName)
                    select new
                    {
                        Name = p.Name.LocalName,
                        IsNull = string.Equals("true", p.Attribute(MetadataNamespace + "null") == null ? null : p.Attribute(MetadataNamespace + "null").Value, StringComparison.OrdinalIgnoreCase),
                        TypeName = p.Attribute(MetadataNamespace + "type") == null ? null : p.Attribute(MetadataNamespace + "type").Value,
                        p.Value
                    };

            foreach (var dp in q)
            {
                // $$$ Could do type marshaling here. 
                string value = dp.Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    value = string.Empty;
                }
                entity.properties[dp.Name] = dp.Value;
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

                writer = new WriterState(_sync);
                _writerMap.Add(partitionKey, writer);                
            }

            try
            {
                _timeWrite.Start();
                writer.WriteAsync(_client, _tableName, dict, partitionKey, rowKey); // Add to this batch, flush this batch when full. 
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
    }

    [DataServiceKey("PartitionKey", "RowKey")]
    internal class GenericEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public IDictionary<string, string> properties = new Dictionary<string, string>();
    }
}