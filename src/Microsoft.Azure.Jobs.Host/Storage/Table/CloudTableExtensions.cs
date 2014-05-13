using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Storage.Table
{
    internal static class CloudTableExtensions
    {
        public static IEnumerable<TElement> QueryByRowKeyPrefix<TElement>(this ICloudTable table, string partitionKey,
            string rowKeyPrefix)
            where TElement : ITableEntity, new()
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            int? noLimit = null;

            return table.Query<TElement>(noLimit,
                new PartitionKeyEquals(partitionKey),
                new RowKeyGreaterThanOrEqual(rowKeyPrefix),
                new RowKeyLessThan(GetNextRowKeyPrefix(rowKeyPrefix))
            );
        }

        public static IEnumerable<TElement> QueryByRowKeyPrefix<TElement>(this ICloudTable table, string partitionKey,
            string rowKeyPrefix, EntityResolver<TElement> resolver)
            where TElement : ITableEntity, new()
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            int? noLimit = null;

            return table.Query<TElement>(noLimit,
                new PartitionKeyEquals(partitionKey),
                new RowKeyGreaterThanOrEqual(rowKeyPrefix),
                new RowKeyLessThan(GetNextRowKeyPrefix(rowKeyPrefix)),
                new Resolver<TElement>(resolver)
            );
        }

        private static string GetNextRowKeyPrefix(string rowKeyPrefix)
        {
            // If rowKeyPrefix is test1, nextRowKeyPrefix would be test2.
            return rowKeyPrefix.Substring(0, rowKeyPrefix.Length - 1) + (char)(rowKeyPrefix[rowKeyPrefix.Length - 1] + 1);
        }
    }
}
