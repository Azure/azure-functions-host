// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal static class StorageTableExtensions
    {
        public static void CreateIfNotExists(this IStorageTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            table.CreateIfNotExistsAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public static bool Exists(this IStorageTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            return table.ExistsAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public static void Insert(this IStorageTable table, ITableEntity entity)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            IStorageTableOperation operation = table.CreateInsertOperation(entity);
            table.ExecuteAsync(operation, CancellationToken.None).GetAwaiter().GetResult();
        }

        public static void Replace(this IStorageTable table, ITableEntity entity)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            IStorageTableOperation operation = table.CreateReplaceOperation(entity);
            table.ExecuteAsync(operation, CancellationToken.None).GetAwaiter().GetResult();
        }


        public static void InsertOrReplace(this IStorageTable table, ITableEntity entity)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            IStorageTableOperation operation = table.CreateInsertOrReplaceOperation(entity);
            table.ExecuteAsync(operation, CancellationToken.None).GetAwaiter().GetResult();
        }


        public static TElement Retrieve<TElement>(this IStorageTable table, string partitionKey, string rowKey)
            where TElement : ITableEntity, new()
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            IStorageTableOperation operation = table.CreateRetrieveOperation<TElement>(partitionKey, rowKey);
            TableResult result = table.ExecuteAsync(operation, CancellationToken.None).GetAwaiter().GetResult();
            return (TElement)result.Result;
        }
    }
}
