// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.Azure.WebJobs.Host.Tables;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Tables
{
    public class TableEntityCollectorBinderTests
    {
        internal class StubTableEntityWriter<T> : TableEntityWriter<T>
            where T : ITableEntity
        {
            public int TimesPartitionFlushed { get; set; }
            public int TimesFlushed { get; set; }

            public StubTableEntityWriter()
                : base(new StorageTable(new CloudTable(new Uri("http://localhost:10000/account/table"))))
            {
                TimesFlushed = 0;
                TimesPartitionFlushed = 0;
            }

            public override Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                TimesFlushed++;
                return base.FlushAsync(cancellationToken);
            }

            internal override Task ExecuteBatchAndCreateTableIfNotExistsAsync(
                Dictionary<string, IStorageTableOperation> batch, CancellationToken cancellationToken)
            {
                // Do nothing
                TimesPartitionFlushed++;

                return Task.FromResult(0);
            }
        }

        [Fact]
        public void ValueHasNotChanged()
        {
            // Arrange
            IStorageTableClient client = new StorageAccount(CloudStorageAccount.DevelopmentStorageAccount).CreateTableClient();
            IStorageTable table = client.GetTableReference("table");
            StubTableEntityWriter<DynamicTableEntity> writer = new StubTableEntityWriter<DynamicTableEntity>();
            Type valueType = typeof(TableEntityWriter<DynamicTableEntity>);
            TableEntityCollectorBinder<DynamicTableEntity> product = new TableEntityCollectorBinder<DynamicTableEntity>(table, writer, valueType);

            // Act
            var parameterLog = product.GetStatus() as TableParameterLog;

            // Assert
            Assert.Null(parameterLog);
        }

        [Fact]
        public void PropertyHasBeenAdded()
        {
            // Arrange
            IStorageTableClient client = new StorageAccount(CloudStorageAccount.DevelopmentStorageAccount).CreateTableClient();
            IStorageTable table = client.GetTableReference("table");
            StubTableEntityWriter<DynamicTableEntity> writer = new StubTableEntityWriter<DynamicTableEntity>();
            Type valueType = typeof(TableEntityWriter<DynamicTableEntity>);
            TableEntityCollectorBinder<DynamicTableEntity> product = new TableEntityCollectorBinder<DynamicTableEntity>(table, writer, valueType);

            DynamicTableEntity value = new DynamicTableEntity
            {
                PartitionKey = "PK",
                RowKey = "RK",
                Properties = new Dictionary<string, EntityProperty> { { "Item", new EntityProperty("Foo") } }
            };

            writer.Add(value);

            // Act
            var parameterLog = product.GetStatus() as TableParameterLog;

            // Assert
            Assert.Equal(1, parameterLog.EntitiesWritten);
            Assert.Equal(0, writer.TimesPartitionFlushed);
        }

        [Fact]
        public void MaximumBatchSizeFlushes()
        {
            // Arrange
            IStorageTableClient client = new StorageAccount(CloudStorageAccount.DevelopmentStorageAccount).CreateTableClient();
            IStorageTable table = client.GetTableReference("table");
            StubTableEntityWriter<DynamicTableEntity> writer = new StubTableEntityWriter<DynamicTableEntity>();
            Type valueType = typeof(TableEntityWriter<DynamicTableEntity>);
            TableEntityCollectorBinder<DynamicTableEntity> product = new TableEntityCollectorBinder<DynamicTableEntity>(table, writer, valueType);

            DynamicTableEntity value = new DynamicTableEntity
            {
                PartitionKey = "PK",
                RowKey = "RK",
                Properties = new Dictionary<string, EntityProperty> { { "Item", new EntityProperty("Foo") } }
            };

            for (int i = 0; i < TableEntityWriter<ITableEntity>.MaxBatchSize + 1; i++)
            {
                value.RowKey = "RK" + i;
                writer.Add(value);
            }

            // Act
            var parameterLog = product.GetStatus() as TableParameterLog;

            // Assert
            Assert.Equal(TableEntityWriter<ITableEntity>.MaxBatchSize + 1, parameterLog.EntitiesWritten);
            Assert.Equal(1, writer.TimesPartitionFlushed);
        }

        [Fact]
        public void MaximumPartitionWidthFlushes()
        {
            // Arrange
            IStorageTableClient client = new StorageAccount(CloudStorageAccount.DevelopmentStorageAccount).CreateTableClient();
            IStorageTable table = client.GetTableReference("table");
            StubTableEntityWriter<DynamicTableEntity> writer = new StubTableEntityWriter<DynamicTableEntity>();
            Type valueType = typeof(TableEntityWriter<DynamicTableEntity>);
            TableEntityCollectorBinder<DynamicTableEntity> product = new TableEntityCollectorBinder<DynamicTableEntity>(table, writer, valueType);

            DynamicTableEntity value = new DynamicTableEntity
            {
                PartitionKey = "PK",
                RowKey = "RK",
                Properties = new Dictionary<string, EntityProperty> { { "Item", new EntityProperty("Foo") } }
            };

            for (int i = 0; i < TableEntityWriter<ITableEntity>.MaxPartitionWidth + 1; i++)
            {
                value.PartitionKey = "PK" + i;
                writer.Add(value);
            }

            // Act
            var parameterLog = product.GetStatus() as TableParameterLog;

            // Assert
            Assert.Equal(TableEntityWriter<ITableEntity>.MaxPartitionWidth + 1, parameterLog.EntitiesWritten);
            Assert.Equal(1, writer.TimesFlushed);
            Assert.Equal(TableEntityWriter<ITableEntity>.MaxPartitionWidth, writer.TimesPartitionFlushed);
        }

        [Fact]
        public void PropertyHasBeenReplaced()
        {
            // Arrange
            IStorageTableClient client = new StorageAccount(CloudStorageAccount.DevelopmentStorageAccount).CreateTableClient();
            IStorageTable table = client.GetTableReference("table");
            StubTableEntityWriter<DynamicTableEntity> writer = new StubTableEntityWriter<DynamicTableEntity>();
            Type valueType = typeof(TableEntityWriter<DynamicTableEntity>);
            TableEntityCollectorBinder<DynamicTableEntity> product = new TableEntityCollectorBinder<DynamicTableEntity>(table, writer, valueType);

            DynamicTableEntity value = new DynamicTableEntity
            {
                PartitionKey = "PK",
                RowKey = "RK",
                Properties = new Dictionary<string, EntityProperty> { { "Item", new EntityProperty("Foo") } }
            };

            writer.Add(value);

            // Act
            var parameterLog = product.GetStatus() as TableParameterLog;

            // Assert
            Assert.Equal(1, parameterLog.EntitiesWritten);
            Assert.Equal(0, writer.TimesPartitionFlushed);

            // Calling again should yield no changes
            Assert.Equal(1, parameterLog.EntitiesWritten);

            // Assert
            Assert.Equal(0, writer.TimesPartitionFlushed);

            // Add same value again.
            writer.Add(value);

            // Act
            parameterLog = product.GetStatus() as TableParameterLog;

            // Assert
            Assert.Equal(2, parameterLog.EntitiesWritten);
            Assert.Equal(1, writer.TimesPartitionFlushed);
        }
    }
}
