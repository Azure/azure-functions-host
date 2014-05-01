using System.Linq;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Storage.Table
{
    internal interface IQueryModifier
    {
        IQueryable<T> Apply<T>(IQueryable<T> q) where T : ITableEntity;
    }

    internal class PartitionKeyEquals : IQueryModifier
    {
        private readonly string _partitionKey;

        public PartitionKeyEquals(string patitionKey)
        {
            _partitionKey = patitionKey;
        }

        public IQueryable<T> Apply<T>(IQueryable<T> q) where T : ITableEntity
        {
            return q.Where(e => e.PartitionKey == _partitionKey);
        }
    }

    internal class RowKeyLessThan : IQueryModifier
    {
        private readonly string _rowKeyExclusiveUpperBound;

        public RowKeyLessThan(string rowKeyExclusiveUpperBound)
        {
            _rowKeyExclusiveUpperBound = rowKeyExclusiveUpperBound;
        }

        public IQueryable<T> Apply<T>(IQueryable<T> q) where T : ITableEntity
        {
            return q.Where(e => e.RowKey.CompareTo(_rowKeyExclusiveUpperBound) < 0);
        }
    }

    internal class RowKeyGreaterThan : IQueryModifier
    {
        private readonly string _rowKeyExclusiveLowerBound;

        public RowKeyGreaterThan(string rowKeyExclusiveLowerBound)
        {
            _rowKeyExclusiveLowerBound = rowKeyExclusiveLowerBound;
        }

        public IQueryable<T> Apply<T>(IQueryable<T> q) where T : ITableEntity
        {
            return q.Where(e => e.RowKey.CompareTo(_rowKeyExclusiveLowerBound) > 0);
        }
    }

    internal class RowKeyGreaterThanOrEqual : IQueryModifier
    {
        private readonly string _rowKeyInclusiveLowerBound;

        public RowKeyGreaterThanOrEqual(string rowKeyInclusiveLowerBound)
        {
            _rowKeyInclusiveLowerBound = rowKeyInclusiveLowerBound;
        }

        public IQueryable<T> Apply<T>(IQueryable<T> q) where T : ITableEntity
        {
            return q.Where(e => e.RowKey.CompareTo(_rowKeyInclusiveLowerBound) >= 0);
        }
    }
}