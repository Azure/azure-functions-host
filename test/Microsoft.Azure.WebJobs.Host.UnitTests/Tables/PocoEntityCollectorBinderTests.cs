// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Tables;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

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
                : base(null)
            {
            }

            internal override Task ExecuteBatchAndCreateTableIfNotExistsAsync(Dictionary<string, TableOperation> batch)
            {
                // Do nothing
                return Task.FromResult(0);
            }
        }

        [Fact]
        public void ValueHasNotChanged()
        {
            // Arrange
            CloudTableClient client = CloudStorageAccount.DevelopmentStorageAccount.CreateCloudTableClient();
            CloudTable table = client.GetTableReference("table");
            PocoEntityWriter<SimpleClass> writer = new PocoEntityWriter<SimpleClass>(table);
            writer.TableEntityWriter = new StubTableEntityWriter();
            Type valueType = typeof(PocoEntityWriter<SimpleClass>);
            PocoEntityCollectorBinder<SimpleClass> product = new PocoEntityCollectorBinder<SimpleClass>(table, writer, valueType);

            // Act
            var parameterLog = product.GetStatus() as TableParameterLog;

            // Assert
            Assert.Null(parameterLog);
        }

        [Fact]
        public void PropertyHasBeenAdded()
        {
            // Arrange
            CloudTableClient client = CloudStorageAccount.DevelopmentStorageAccount.CreateCloudTableClient();
            CloudTable table = client.GetTableReference("table");
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
            CloudTableClient client = CloudStorageAccount.DevelopmentStorageAccount.CreateCloudTableClient();
            CloudTable table = client.GetTableReference("table");
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
    }
}
