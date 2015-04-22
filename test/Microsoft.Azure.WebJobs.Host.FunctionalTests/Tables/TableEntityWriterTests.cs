// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.Azure.WebJobs.Host.Tables;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.Tables
{
    public class TableEntityWriterTests
    {
        [Fact]
        public void FlushAfterAdd_PersistsEntity()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference("Table");

            TableEntityWriter<ITableEntity> product = new TableEntityWriter<ITableEntity>(table);
            const string partitionKey = "PK";
            const string rowKey = "RK";
            DynamicTableEntity entity = new DynamicTableEntity(partitionKey, rowKey);
            product.Add(entity);

            // Act
            product.FlushAsync().GetAwaiter().GetResult();

            // Assert
            DynamicTableEntity persisted = table.Retrieve<DynamicTableEntity>(partitionKey, rowKey);
            Assert.NotNull(persisted);
        }

        private static IStorageAccount CreateFakeStorageAccount()
        {
            return new FakeStorageAccount();
        }
    }
}
