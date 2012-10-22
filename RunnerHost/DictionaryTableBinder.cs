using System;
using System.Collections.Generic;
using System.Reflection;
using AzureTables;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    // Provide an IDictionary interface over an AzureTable
    // Tuple is (Partition, Row) key.
    class DictionaryTableAdapter<T> : ISelfWatch, IDictionary<Tuple<string, string>, T> where T : new()
    {        
        private readonly AzureTable<T> Table;

        public DictionaryTableAdapter(AzureTable<T> table)
        {
            this.Table = table;
        }

        public override string ToString()
        {
            return Table.GetStatus(); // Use selfwatch as ToString
        }

        public void Flush()
        {
            Table.Flush();
        }

        private static string PartitionKey(Tuple<string, string> tuple)
        {
            return tuple.Item1;
        }
        private static string RowKey(Tuple<string, string> tuple)
        {
            return tuple.Item2;
        }
       
        public void Add(Tuple<string, string> key, T value)
        {
            // $$$ Verify correct semantics on insert vs. upsert
            this.Table.Write(PartitionKey(key), RowKey(key), value);
        }

        public bool ContainsKey(Tuple<string, string> key)
        {
            var d = this.Table.Lookup(PartitionKey(key), RowKey(key));
            return d != null;
        }

        public ICollection<Tuple<string, string>> Keys
        {
            get { throw new NotImplementedException(); }
        }

        public bool Remove(Tuple<string, string> key)
        {
            this.Table.Delete(PartitionKey(key), RowKey(key));
            return true; // $$$ Bug - lookup first
        }

        public bool TryGetValue(Tuple<string, string> key, out T value)
        {
            IAzureTableReader<T> t = this.Table;
            value = t.Lookup(PartitionKey(key), RowKey(key));
            
            if (value == null)
            {
                // $$$ bug. Neeed distinction between null and missing
                return false;
            }
            return true;
        }

        public ICollection<T> Values
        {
            get { throw new NotImplementedException(); }
        }

        public T this[Tuple<string, string> key]
        {
            get
            {
                IAzureTableReader<T> t = this.Table;
                T value = t.Lookup(PartitionKey(key), RowKey(key));
                return value;
            }
            set
            {
                Add(key, value);
            }
        }

        public void Add(KeyValuePair<Tuple<string, string>, T> item)
        {
            this.Add(item.Key, item.Value);
        }

        public void Clear()
        {
            this.Table.Clear();
        }

        public bool Contains(KeyValuePair<Tuple<string, string>, T> item)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(KeyValuePair<Tuple<string, string>, T>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public int Count
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsReadOnly
        {
            get { throw new NotImplementedException(); }
        }

        public bool Remove(KeyValuePair<Tuple<string, string>, T> item)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<KeyValuePair<Tuple<string, string>, T>> GetEnumerator()
        {
            return GetEnumeratorWorker();
        }

        private IEnumerator<KeyValuePair<Tuple<string, string>, T>> GetEnumeratorWorker()
        {
            var x =this.Table.EnumerateDict(null);
            return x.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumeratorWorker();
        }

        public string GetStatus()
        {
            return this.Table.GetStatus(); 
        }
    }
}