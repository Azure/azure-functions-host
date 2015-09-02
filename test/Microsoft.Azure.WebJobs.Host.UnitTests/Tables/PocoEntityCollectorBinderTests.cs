// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Tables;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Storage;
using Moq;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Tables
{
    public class PocoEntityCollectorBinderTests
    {
        public class SimpleClass
        {
            public string PartitionKey { get; set; }
            public string RowKey { get; set; }
        }

        internal class StubTableEntityWriter : TableEntityWriter<ITableEntity>
        {
            public StubTableEntityWriter()
                : base(new StorageTable(new CloudTable(new Uri("http://localhost:10000/account/table"))))
            {
            }

            internal override Task ExecuteBatchAndCreateTableIfNotExistsAsync(
                Dictionary<string, IStorageTableOperation> batch, CancellationToken cancellationToken)
            {
                // Do nothing
                return Task.FromResult(0);
            }
        }

        [Fact]
        public void ValueHasNotChanged()
        {
            // Arrange
            IStorageTableClient client = CreateTableClient();
            IStorageTable table = client.GetTableReference("table");
            PocoEntityWriter<SimpleClass> writer = new PocoEntityWriter<SimpleClass>(table);
            writer.TableEntityWriter = new StubTableEntityWriter();
            Type valueType = typeof(PocoEntityWriter<SimpleClass>);
            PocoEntityCollectorBinder<SimpleClass> product = new PocoEntityCollectorBinder<SimpleClass>(table, writer,
                valueType);

            // Act
            var parameterLog = product.GetStatus() as TableParameterLog;

            // Assert
            Assert.Null(parameterLog);
        }


        [Fact]
        public void PropertyHasBeenAdded()
        {
            // Arrange
            IStorageTableClient client = CreateTableClient();
            IStorageTable table = client.GetTableReference("table");
            PocoEntityWriter<SimpleClass> writer = new PocoEntityWriter<SimpleClass>(table);
            writer.TableEntityWriter = new StubTableEntityWriter();
            Type valueType = typeof(PocoEntityWriter<SimpleClass>);
            PocoEntityCollectorBinder<SimpleClass> product = new PocoEntityCollectorBinder<SimpleClass>(table, writer, valueType);

            SimpleClass value = new SimpleClass
            {
                PartitionKey = "PK",
                RowKey = "RK"
            };

            writer.Add(value);

            // Act
            var parameterLog = product.GetStatus() as TableParameterLog;

            // Assert
            Assert.Equal(1, parameterLog.EntitiesWritten);
        }

        [Fact]
        public void PropertyHasBeenReplaced()
        {
            // Arrange
            IStorageTableClient client = CreateTableClient();
            IStorageTable table = client.GetTableReference("table");
            PocoEntityWriter<SimpleClass> writer = new PocoEntityWriter<SimpleClass>(table);
            writer.TableEntityWriter = new StubTableEntityWriter();
            Type valueType = typeof(PocoEntityWriter<SimpleClass>);
            PocoEntityCollectorBinder<SimpleClass> product = new PocoEntityCollectorBinder<SimpleClass>(table, writer, valueType);

            SimpleClass value = new SimpleClass
            {
                PartitionKey = "PK",
                RowKey = "RK"
            };

            writer.Add(value);

            // Act
            var parameterLog = product.GetStatus() as TableParameterLog;

            // Assert
            Assert.Equal(1, parameterLog.EntitiesWritten);

            // Calling again should yield no changes
            parameterLog = product.GetStatus() as TableParameterLog;

            // Assert
            Assert.Equal(1, parameterLog.EntitiesWritten);

            // Add same value again.

            writer.Add(value);

            // Act
            parameterLog = product.GetStatus() as TableParameterLog;

            // Assert
            Assert.Equal(2, parameterLog.EntitiesWritten);
        }

        private IStorageTableClient CreateTableClient()
        {
            Mock<IServiceProvider> services = new Mock<IServiceProvider>(MockBehavior.Strict);
            StorageClientFactory clientFactory = new StorageClientFactory();
            services.Setup(p => p.GetService(typeof(StorageClientFactory))).Returns(clientFactory);
            IStorageTableClient client = new StorageAccount(CloudStorageAccount.DevelopmentStorageAccount, services.Object).CreateTableClient();
            return client;
        }
    }
}
