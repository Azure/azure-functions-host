// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class TableEntityTests
    {
        private const string TriggerQueueName = "input";
        private const string TableName = "Table";
        private const string PartitionKey = "PK";
        private const string RowKey = "RK";

        [Fact]
        public void TableEntity_IfBoundToExistingDynamicTableEntity_Binds()
        {
            // Arrange
            const string expectedKey = "abc";
            const int expectedValue = 123;
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            IStorageTable table = CreateTable(account, TableName);
            Dictionary<string, EntityProperty> properties = new Dictionary<string, EntityProperty>
            {
                { expectedKey, new EntityProperty(expectedValue) }
            };
            table.Insert(new DynamicTableEntity(PartitionKey, RowKey, etag: null, properties: properties));

            // Act
            DynamicTableEntity result = RunTrigger<DynamicTableEntity>(account, typeof(BindToDynamicTableEntityProgram),
                (s) => BindToDynamicTableEntityProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(PartitionKey, result.PartitionKey);
            Assert.Equal(RowKey, result.RowKey);
            Assert.NotNull(result.Properties);
            Assert.True(result.Properties.ContainsKey(expectedKey));
            EntityProperty property = result.Properties[expectedKey];
            Assert.NotNull(property);
            Assert.Equal(EdmType.Int32, property.PropertyType);
            Assert.Equal(expectedValue, property.Int32Value);
        }

        [Fact]
        public void TableEntity_IfBoundToExistingPoco_Binds()
        {
            // Arrange
            const string expectedValue = "abc";
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            IStorageTable table = CreateTable(account, TableName);
            Dictionary<string, EntityProperty> properties = new Dictionary<string, EntityProperty>
            {
                { "Value", new EntityProperty(expectedValue) }
            };
            table.Insert(new DynamicTableEntity(PartitionKey, RowKey, etag: null, properties: properties));

            // Act
            Poco result = RunTrigger<Poco>(account, typeof(BindToPocoProgram),
                (s) => BindToPocoProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedValue, result.Value);
        }

        [Fact]
        public void TableEntity_IfUpdatesPoco_Persists()
        {
            // Arrange
            const string originalValue = "abc";
            const string expectedValue = "def";
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(expectedValue));

            IStorageTable table = CreateTable(account, TableName);
            Dictionary<string, EntityProperty> originalProperties = new Dictionary<string, EntityProperty>
            {
                { "Value", new EntityProperty(originalValue) }
            };
            table.Insert(new DynamicTableEntity(PartitionKey, RowKey, etag: null, properties: originalProperties));

            // Act
            RunTrigger(account, typeof(UpdatePocoProgram));

            // Assert
            DynamicTableEntity entity = table.Retrieve<DynamicTableEntity>(PartitionKey, RowKey);
            Assert.NotNull(entity);
            IDictionary<string, EntityProperty> properties = entity.Properties;
            Assert.NotNull(properties);
            Assert.True(properties.ContainsKey("Value"));
            EntityProperty property = properties["Value"];
            Assert.NotNull(property);
            Assert.Equal(EdmType.String, property.PropertyType);
            Assert.Equal(expectedValue, property.StringValue);
        }

        [Fact]
        public void TableEntity_IfBoundToExistingPoco_BindsUsingNativeTableTypes()
        {
            // Arrange
            byte[] expectedValue = new byte[] { 0x12, 0x34 };
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            IStorageTable table = CreateTable(account, TableName);
            Dictionary<string, EntityProperty> properties = new Dictionary<string, EntityProperty>
            {
                { "Value", new EntityProperty(expectedValue) }
            };
            table.Insert(new DynamicTableEntity(PartitionKey, RowKey, etag: null, properties: properties));

            // Act
            PocoWithByteArrayValue result = RunTrigger<PocoWithByteArrayValue>(account,
                typeof(BindToPocoWithByteArrayValueProgram), (s) => BindToPocoWithByteArrayValueProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedValue, result.Value);
        }

        [Fact]
        public void TableEntity_IfUpdatesPoco_PersistsUsingNativeTableTypes()
        {
            // Arrange
            byte[] originalValue = new byte[] { 0x12, 0x34 };
            byte[] expectedValue = new byte[] { 0x56, 0x78 };
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(expectedValue));

            IStorageTable table = CreateTable(account, TableName);
            Dictionary<string, EntityProperty> originalProperties = new Dictionary<string, EntityProperty>
            {
                { "Value", new EntityProperty(originalValue) }
            };
            table.Insert(new DynamicTableEntity(PartitionKey, RowKey, etag: null, properties: originalProperties));

            // Act
            RunTrigger(account, typeof(UpdatePocoWithByteArrayValueProgram));

            // Assert
            DynamicTableEntity entity = table.Retrieve<DynamicTableEntity>(PartitionKey, RowKey);
            Assert.NotNull(entity);
            IDictionary<string, EntityProperty> properties = entity.Properties;
            Assert.NotNull(properties);
            Assert.True(properties.ContainsKey("Value"));
            EntityProperty property = properties["Value"];
            Assert.NotNull(property);
            Assert.Equal(EdmType.Binary, property.PropertyType);
            Assert.Equal(expectedValue, property.BinaryValue);
        }

        [Fact]
        public void TableEntity_IfUpdatesPartitionKey_Throws()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            IStorageTable table = CreateTable(account, TableName);
            table.Insert(new DynamicTableEntity(PartitionKey, RowKey));

            // Act
            Exception exception = RunTriggerFailure(account, typeof(UpdatePocoPartitionKeyProgram));

            // Assert
            Assert.NotNull(exception);
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal("Error while handling parameter entity after function returned:", exception.Message);
            Exception innerException = exception.InnerException;
            Assert.NotNull(innerException);
            Assert.IsType<InvalidOperationException>(innerException);
            Assert.Equal("When binding to a table entity, the partition key must not be changed.",
                innerException.Message);
        }

        [Fact]
        public void TableEntity_IfUpdatesRowKey_Throws()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            IStorageTable table = CreateTable(account, TableName);
            table.Insert(new DynamicTableEntity(PartitionKey, RowKey));

            // Act
            Exception exception = RunTriggerFailure(account, typeof(UpdatePocoRowKeyProgram));

            // Assert
            Assert.NotNull(exception);
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal("Error while handling parameter entity after function returned:", exception.Message);
            Exception innerException = exception.InnerException;
            Assert.NotNull(innerException);
            Assert.IsType<InvalidOperationException>(innerException);
            Assert.Equal("When binding to a table entity, the row key must not be changed.", innerException.Message);
        }

        [Fact]
        public void TableEntity_IfBoundUsingRouteParameters_Binds()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            const string tableName = TableName + "B";
            const string partitionKey = PartitionKey + "B";
            const string rowKey = RowKey + "B";
            TableEntityMessage message = new TableEntityMessage
            {
                TableName = tableName,
                PartitionKey = partitionKey,
                RowKey = rowKey
            };
            triggerQueue.AddMessage(triggerQueue.CreateMessage(JsonConvert.SerializeObject(message)));

            IStorageTable table = CreateTable(account, tableName);
            Dictionary<string, EntityProperty> originalProperties = new Dictionary<string, EntityProperty>
            {
                { "Value", new EntityProperty(123) }
            };
            table.Insert(new DynamicTableEntity(partitionKey, rowKey, etag: null, properties: originalProperties));

            // Act
            RunTrigger(account, typeof(BindUsingRouteParametersProgram));

            // Assert
            DynamicTableEntity entity = table.Retrieve<DynamicTableEntity>(partitionKey, rowKey);
            Assert.NotNull(entity);
            IDictionary<string, EntityProperty> properties = entity.Properties;
            Assert.NotNull(properties);
            Assert.True(properties.ContainsKey("Value"));
            EntityProperty property = properties["Value"];
            Assert.NotNull(property);
            Assert.Equal(EdmType.Int32, property.PropertyType);
            Assert.True(property.Int32Value.HasValue);
            Assert.Equal(456, property.Int32Value.Value);
        }

        private static IStorageAccount CreateFakeStorageAccount()
        {
            return new FakeStorageAccount();
        }

        private static IStorageQueue CreateQueue(IStorageAccount account, string queueName)
        {
            IStorageQueueClient client = account.CreateQueueClient();
            IStorageQueue queue = client.GetQueueReference(queueName);
            queue.CreateIfNotExists();
            return queue;
        }

        private static IStorageTable CreateTable(IStorageAccount account, string tableName)
        {
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(tableName);
            table.CreateIfNotExists();
            return table;
        }

        private static void RunTrigger(IStorageAccount account, Type programType)
        {
            FunctionalTest.RunTrigger(account, programType);
        }

        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return FunctionalTest.RunTrigger<TResult>(account, programType, setTaskSource);
        }

        private static Exception RunTriggerFailure(IStorageAccount account, Type programType)
        {
            return FunctionalTest.RunTriggerFailure(account, programType);
        }

        private class BindToDynamicTableEntityProgram
        {
            public static TaskCompletionSource<DynamicTableEntity> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Table(TableName, PartitionKey, RowKey)] DynamicTableEntity entity)
            {
                TaskSource.TrySetResult(entity);
            }
        }

        private class BindToPocoProgram
        {
            public static TaskCompletionSource<Poco> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Table(TableName, PartitionKey, RowKey)] Poco entity)
            {
                TaskSource.TrySetResult(entity);
            }
        }

        private class UpdatePocoProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage message,
                [Table(TableName, PartitionKey, RowKey)] Poco entity)
            {
                entity.Value = message.AsString;
            }
        }

        private class BindToPocoWithByteArrayValueProgram
        {
            public static TaskCompletionSource<PocoWithByteArrayValue> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Table(TableName, PartitionKey, RowKey)] PocoWithByteArrayValue entity)
            {
                TaskSource.TrySetResult(entity);
            }
        }

        private class UpdatePocoWithByteArrayValueProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage message,
                [Table(TableName, PartitionKey, RowKey)] PocoWithByteArrayValue entity)
            {
                entity.Value = message.AsBytes;
            }
        }

        private class UpdatePocoPartitionKeyProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Table(TableName, PartitionKey, RowKey)] PocoWithKeys entity)
            {
                entity.PartitionKey = Guid.NewGuid().ToString();
            }
        }

        private class UpdatePocoRowKeyProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Table(TableName, PartitionKey, RowKey)] PocoWithKeys entity)
            {
                entity.RowKey = Guid.NewGuid().ToString();
            }
        }

        private class BindUsingRouteParametersProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] TableEntityMessage message,
                [Table("{TableName}", "{PartitionKey}", "{RowKey}")] SdkTableEntity entity)
            {
                entity.Value = 456;
            }
        }

        private class Poco
        {
            public string Value { get; set; }
        }

        private class PocoWithByteArrayValue
        {
            public byte[] Value { get; set; }
        }

        private class PocoWithKeys
        {
            public string PartitionKey { get; set; }

            public string RowKey { get; set; }
        }

        private class TableEntityMessage
        {
            public string TableName { get; set; }

            public string PartitionKey { get; set; }

            public string RowKey { get; set; }
        }

        private class SdkTableEntity : TableEntity
        {
            public int Value { get; set; }
        }
    }
}
