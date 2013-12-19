using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;

namespace AzureTables
{
    // Typesafe wrappers.
    internal class AzureTable<TPartRowKey, TValue> : AzureTable, IAzureTableReader<TValue> where TValue : new()
    {
        private readonly Func<TPartRowKey, Tuple<string, string>> _funcGetRowPartKey;

        // Helper for when we have a constant partition key 
        public AzureTable(CloudStorageAccount account, string tableName, string constPartKey)
            : this(account, tableName, rowKey => Tuple.Create(constPartKey, rowKey.ToString()))
        {
        }

        public AzureTable(CloudStorageAccount account, string tableName, Func<TPartRowKey, Tuple<string, string>> funcGetRowPartKey)
            : this(new LiveTableCore(account, tableName), funcGetRowPartKey)
        {
        }

        internal AzureTable(TableCore core, Func<TPartRowKey, Tuple<string, string>> funcGetRowPartKey)
            : base(core)
        {
            _funcGetRowPartKey = funcGetRowPartKey;
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
}
