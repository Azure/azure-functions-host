// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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

        public static void Insert(this IStorageTable table, ITableEntity entity)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            IStorageTableBatchOperation batch = table.CreateBatch();
            batch.Insert(entity);
            table.ExecuteBatchAsync(batch, CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
