using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace RunnerHost
{
    public class PartitionRowKey
    {
        public string RowKey { get; set; }
        public string PartitionKey { get; set; }

        public override string ToString()
        {
            return string.Format("{0}:{1}", PartitionKey, RowKey);
        }

        public override int GetHashCode()
        {
            return RowKey.GetHashCode() ^ PartitionKey.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            PartitionRowKey other = obj as PartitionRowKey;
            if (other == null)
            {
                return false;
            }

            return string.Equals(this.RowKey, other.RowKey) && string.Equals(this.PartitionKey, other.PartitionKey);
        }
    }

    class TableBinder<TValue> : IDictionary<PartitionRowKey, TValue>
    {
        private bool _canRead = true;
        private bool _canWrite = true;

        private readonly CloudStorageAccount _account;
        private readonly string _tableName;

        public TableBinder(CloudStorageAccount account, string tableName)
        {
            _account = account;
            _tableName = tableName;

            CloudTableClient client = account.CreateCloudTableClient();
            client.CreateTableIfNotExist(_tableName);
        }

        private void EnsureCanRead()
        {
            if (!_canRead)
            {
                throw new InvalidOperationException("Table is not readable");
            }
        }
        private void EnsureCanWrite()
        {
            if (!_canWrite)
            {
                throw new InvalidOperationException("Table is not writeable");
            }
        }

        public void Add(PartitionRowKey key, TValue value)
        {
            EnsureCanWrite();
            throw new NotImplementedException();
        }

        public bool ContainsKey(PartitionRowKey key)
        {
            throw new NotImplementedException();
        }

        public ICollection<PartitionRowKey> Keys
        {
            get { throw new NotImplementedException(); }
        }

        public bool Remove(PartitionRowKey key)
        {
            throw new NotImplementedException();
        }

        public bool TryGetValue(PartitionRowKey key, out TValue value)
        {
            throw new NotImplementedException();
        }

        public ICollection<TValue> Values
        {
            get { throw new NotImplementedException(); }
        }

        public TValue this[PartitionRowKey key]
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public void Add(KeyValuePair<PartitionRowKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            Utility.DeleteTable(this._account, _tableName);            
        }

        public bool Contains(KeyValuePair<PartitionRowKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(KeyValuePair<PartitionRowKey, TValue>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public int Count
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsReadOnly
        {
            get { return _canRead && !_canWrite; }
        }

        public bool Remove(KeyValuePair<PartitionRowKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<KeyValuePair<PartitionRowKey, TValue>> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}
