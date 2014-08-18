// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Provides extension methods for <see cref="ICloudTable"/>.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public static class CloudTableExtensions
#else
    internal static class CloudTableExtensions
#endif
    {
        /// <summary>Queries the table by a row key prefix.</summary>
        /// <typeparam name="TElement">The type of entities to query.</typeparam>
        /// <param name="table">The table to query.</param>
        /// <param name="partitionKey">The partition key by which to filter.</param>
        /// <param name="rowKeyPrefix">The row key prefix by which to filter.</param>
        /// <returns>The matching entities.</returns>
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
                new PartitionKeyEqualsQueryModifier(partitionKey),
                new RowKeyGreaterThanOrEqualQueryModifier(rowKeyPrefix),
                new RowKeyLessThanQueryModifier(GetNextRowKeyPrefix(rowKeyPrefix))
            );
        }

        /// <summary>Queries the table by a row key prefix.</summary>
        /// <typeparam name="TElement">The type of entities to query.</typeparam>
        /// <param name="table">The table to query.</param>
        /// <param name="partitionKey">The partition key by which to filter.</param>
        /// <param name="rowKeyPrefix">The row key prefix by which to filter.</param>
        /// <param name="resolver">The entity resolver to use to resolve entities.</param>
        /// <returns>The matching entities.</returns>
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
                new PartitionKeyEqualsQueryModifier(partitionKey),
                new RowKeyGreaterThanOrEqualQueryModifier(rowKeyPrefix),
                new RowKeyLessThanQueryModifier(GetNextRowKeyPrefix(rowKeyPrefix)),
                new ResolverQueryModifier<TElement>(resolver)
            );
        }

        private static string GetNextRowKeyPrefix(string rowKeyPrefix)
        {
            // If rowKeyPrefix is test1, nextRowKeyPrefix would be test2.
            return rowKeyPrefix.Substring(0, rowKeyPrefix.Length - 1) + (char)(rowKeyPrefix[rowKeyPrefix.Length - 1] + 1);
        }
    }
}
