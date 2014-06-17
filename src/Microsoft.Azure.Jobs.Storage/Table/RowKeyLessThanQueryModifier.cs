using System;
using System.Linq;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.Jobs.Storage.Table
#else
namespace Microsoft.Azure.Jobs.Host.Storage.Table
#endif
{
    /// <summary>Represents a query modifier that filters by row keys less than a value.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class RowKeyLessThanQueryModifier : IQueryModifier
#else
    internal class RowKeyLessThanQueryModifier : IQueryModifier
#endif
    {
        private readonly string _rowKeyExclusiveUpperBound;

        /// <summary>Initializes a new instance of the <see cref="RowKeyLessThanQueryModifier"/> class.</summary>
        /// <param name="rowKeyExclusiveUpperBound">The exclusive upper bound for the row key.</param>
        public RowKeyLessThanQueryModifier(string rowKeyExclusiveUpperBound)
        {
            _rowKeyExclusiveUpperBound = rowKeyExclusiveUpperBound;
        }

        /// <inheritdoc />
        public IQueryable<T> Apply<T>(IQueryable<T> q) where T : ITableEntity
        {
            return q.Where(e => e.RowKey.CompareTo(_rowKeyExclusiveUpperBound) < 0);
        }
    }
}
