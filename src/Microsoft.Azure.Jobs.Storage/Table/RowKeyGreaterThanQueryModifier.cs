using System;
using System.Linq;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.Jobs.Storage.Table
#else
namespace Microsoft.Azure.Jobs.Host.Storage.Table
#endif
{
    /// <summary>Represents a query modifier that filters by row keys greater than a value.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class RowKeyGreaterThanQueryModifier : IQueryModifier
#else
    internal class RowKeyGreaterThanQueryModifier : IQueryModifier
#endif
    {
        private readonly string _rowKeyExclusiveLowerBound;

        /// <summary>Initializes a new instance of the <see cref="RowKeyGreaterThanQueryModifier"/> class.</summary>
        /// <param name="rowKeyExclusiveLowerBound">The exclusive lower bound for the row key.</param>
        public RowKeyGreaterThanQueryModifier(string rowKeyExclusiveLowerBound)
        {
            _rowKeyExclusiveLowerBound = rowKeyExclusiveLowerBound;
        }

        /// <inheritdoc />
        public IQueryable<T> Apply<T>(IQueryable<T> q) where T : ITableEntity
        {
            return q.Where(e => e.RowKey.CompareTo(_rowKeyExclusiveLowerBound) > 0);
        }
    }
}
