using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json;

namespace AzureTables
{
    // Wrapper for when we want to read as a strong type.
    // $$$ Should this implement IDictionary<Tuple<string,string>, TValue> ?
    internal class AzureTable<TValue> : AzureTable, IAzureTable<TValue> where TValue : new()
    {
        public AzureTable(CloudStorageAccount account, string tableName)
            : base(account, tableName)
        {
        }

        internal AzureTable(TableCore core)
            : base(core)
        {
        }

        public static new AzureTable<TValue> NewInMemory()
        {
            return new AzureTable<TValue>(new LocalTableCore());
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
                   let obj = ParseOrNull<TValue>(item)
                   where obj != null
                   select obj;
        }

        static T ParseOrNull<T>(IDictionary<string, string> item) where T : new()
        {
            try
            {
                return ObjectBinderHelpers.ConvertDictToObject<T>(item);
            }
            catch (JsonSerializationException)
            {
                return default(T);
            }
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
}
