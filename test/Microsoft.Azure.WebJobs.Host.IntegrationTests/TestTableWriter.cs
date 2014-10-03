// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.Azure.WebJobs.Host.Tables;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.IntegrationTests
{
    internal static class TestTableWriter
    {
        public static void FlushTable(CloudStorageAccount account, string tableName, ITableEntity entity)
        {
            IStorageTableClient client = new StorageAccount(account).CreateTableClient();
            IStorageTable table = client.GetTableReference(tableName);
            TableEntityWriter<ITableEntity> writer = new TableEntityWriter<ITableEntity>(table);
            writer.Add(entity);
            writer.FlushAsync().Wait();
        }
    }
}
