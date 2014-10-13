// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
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
    public class TableTests
    {
        private const string TriggerQueueName = "input";
        private const string TableName = "Table";
        private const string PartitionKey = "PK";
        private const string RowKey = "RK";
        private const string PropertyName = "Property";

        [Fact]
        public void Table_IfBoundToCloudTable_BindsAndCreatesTable()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            // Act
            CloudTable result = RunTrigger<CloudTable>(account, typeof(BindToCloudTableProgram),
                (s) => BindToCloudTableProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(TableName, result.Name);
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            Assert.True(table.Exists());
        }

        [Fact]
        public void Table_IfBoundToICollectorITableEntity_AddInsertsEntity()
        {
            // Arrange
            const string expectedValue = "abc";
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(expectedValue));

            // Act
            RunTrigger(account, typeof(BindToICollectorITableEntityProgram));

            // Assert
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            Assert.True(table.Exists());
            DynamicTableEntity entity = table.Retrieve<DynamicTableEntity>(PartitionKey, RowKey);
            Assert.NotNull(entity);
            Assert.NotNull(entity.Properties);
            Assert.True(entity.Properties.ContainsKey(PropertyName));
            EntityProperty property = entity.Properties[PropertyName];
            Assert.NotNull(property);
            Assert.Equal(EdmType.String, property.PropertyType);
            Assert.Equal(expectedValue, property.StringValue);
        }

        [Fact]
        public void Table_IfBoundToICollectorPoco_AddInsertsEntity()
        {
            // Arrange
            const string expectedValue = "abc";
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(expectedValue));

            // Act
            RunTrigger(account, typeof(BindToICollectorPocoProgram));

            // Assert
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            Assert.True(table.Exists());
            DynamicTableEntity entity = table.Retrieve<DynamicTableEntity>(PartitionKey, RowKey);
            Assert.NotNull(entity);
            Assert.NotNull(entity.Properties);
            Assert.True(entity.Properties.ContainsKey(PropertyName));
            EntityProperty property = entity.Properties[PropertyName];
            Assert.NotNull(property);
            Assert.Equal(EdmType.String, property.PropertyType);
            Assert.Equal(expectedValue, property.StringValue);
        }

        [Fact]
        public void Table_IfBoundToICollectorPoco_AddInsertsUsingNativeTableTypes()
        {
            // Arrange
            PocoWithAllTypes expected = new PocoWithAllTypes
            {
                PartitionKey = PartitionKey,
                RowKey = RowKey,
                BooleanProperty = true,
                NullableBooleanProperty = null,
                ByteArrayProperty = new byte[] { 0x12, 0x34 },
                DateTimeProperty = DateTime.Now,
                NullableDateTimeProperty = null,
                DateTimeOffsetProperty = DateTimeOffset.MaxValue,
                NullableDateTimeOffsetProperty = null,
                DoubleProperty = 3.14,
                NullableDoubleProperty = null,
                GuidProperty = Guid.NewGuid(),
                NullableGuidProperty = null,
                Int32Property = 123,
                NullableInt32Property = null,
                Int64Property = 456,
                NullableInt64Property = null,
                StringProperty = "abc",
                PocoProperty = new Poco
                {
                    PartitionKey = "def",
                    RowKey = "ghi",
                    Property = "jkl"
                }
            };
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(JsonConvert.SerializeObject(expected)));

            // Act
            RunTrigger(account, typeof(BindToICollectorPocoWithAllTypesProgram));

            // Assert
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            Assert.True(table.Exists());
            DynamicTableEntity entity = table.Retrieve<DynamicTableEntity>(PartitionKey, RowKey);
            Assert.Equal(expected.PartitionKey, entity.PartitionKey);
            Assert.Equal(expected.RowKey, entity.RowKey);
            IDictionary<string, EntityProperty> properties = entity.Properties;
            AssertNullablePropertyEqual(expected.BooleanProperty, EdmType.Boolean, properties, "BooleanProperty",
                (p) => p.BooleanValue);
            AssertPropertyNull(EdmType.Boolean, properties, "NullableBooleanProperty", (p) => p.BooleanValue);
            AssertPropertyEqual(expected.ByteArrayProperty, EdmType.Binary, properties, "ByteArrayProperty",
                (p) => p.BinaryValue);
            AssertNullablePropertyEqual(expected.DateTimeProperty, EdmType.DateTime, properties, "DateTimeProperty",
                (p) => p.DateTime);
            AssertPropertyNull(EdmType.DateTime, properties, "NullableDateTimeProperty", (p) => p.DateTime);
            AssertNullablePropertyEqual(expected.DateTimeOffsetProperty, EdmType.DateTime, properties,
                "DateTimeOffsetProperty", (p) => p.DateTime);
            AssertPropertyNull(EdmType.DateTime, properties, "NullableDateTimeOffsetProperty",
                (p) => p.DateTimeOffsetValue);
            AssertNullablePropertyEqual(expected.DoubleProperty, EdmType.Double, properties, "DoubleProperty",
                (p) => p.DoubleValue);
            AssertPropertyNull(EdmType.Double, properties, "NullableDoubleProperty", (p) => p.DoubleValue);
            AssertNullablePropertyEqual(expected.GuidProperty, EdmType.Guid, properties, "GuidProperty",
                (p) => p.GuidValue);
            AssertPropertyNull(EdmType.Guid, properties, "NullableGuidProperty", (p) => p.GuidValue);
            AssertNullablePropertyEqual(expected.Int32Property, EdmType.Int32, properties, "Int32Property",
                (p) => p.Int32Value);
            AssertPropertyNull(EdmType.Int32, properties, "NullableInt32Property", (p) => p.Int32Value);
            AssertNullablePropertyEqual(expected.Int64Property, EdmType.Int64, properties, "Int64Property",
                (p) => p.Int64Value);
            AssertPropertyNull(EdmType.Int64, properties, "NullableInt64Property", (p) => p.Int64Value);
            AssertPropertyEqual(expected.StringProperty, EdmType.String, properties, "StringProperty",
                (p) => p.StringValue);
            AssertPropertyEqual(JsonConvert.SerializeObject(expected.PocoProperty, Formatting.Indented), EdmType.String,
                properties, "PocoProperty", (p) => p.StringValue);
        }

        [Fact]
        public void Table_IfBoundToIQueryableDynamicTableEntityAndDoesNotExist_BindsAndDoesNotCreateTable()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            // Act
            IQueryable<DynamicTableEntity> result = RunTrigger<IQueryable<DynamicTableEntity>>(account,
                typeof(BindToIQueryableDynamicTableEntityProgram),
                (s) => BindToIQueryableDynamicTableEntityProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            Assert.False(table.Exists());
        }

        [Fact]
        public void Table_IfBoundToIQueryableDynamicTableEntityAndExists_Binds()
        {
            // Arrange
            Guid expectedValue = Guid.NewGuid();
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));
            IStorageTableClient client = account.CreateTableClient();
            IStorageTable table = client.GetTableReference(TableName);
            table.CreateIfNotExists();
            Dictionary<string, EntityProperty> properties = new Dictionary<string, EntityProperty>
            {
                { PropertyName, new EntityProperty(expectedValue) }
            };
            table.Insert(new DynamicTableEntity(PartitionKey, RowKey, etag: null, properties: properties));

            // Act
            IQueryable<DynamicTableEntity> result = RunTrigger<IQueryable<DynamicTableEntity>>(account,
                typeof(BindToIQueryableDynamicTableEntityProgram),
                (s) => BindToIQueryableDynamicTableEntityProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            DynamicTableEntity[] entities = result.ToArray();
            Assert.Equal(1, entities.Length);
            DynamicTableEntity entity = entities[0];
            Assert.NotNull(entity);
            Assert.Equal(PartitionKey, entity.PartitionKey);
            Assert.Equal(RowKey, entity.RowKey);
            Assert.NotNull(entity.Properties);
            Assert.True(entity.Properties.ContainsKey(PropertyName));
            EntityProperty property = entity.Properties[PropertyName];
            Assert.NotNull(property);
            Assert.Equal(EdmType.Guid, property.PropertyType);
            Assert.Equal(expectedValue, property.GuidValue);
        }

        private static void AssertNullablePropertyEqual<T>(T expected,
            EdmType expectedType,
            IDictionary<string, EntityProperty> properties,
            string propertyName,
            Func<EntityProperty, Nullable<T>> actualAccessor)
            where T : struct
        {
            Assert.NotNull(properties);
            Assert.True(properties.ContainsKey(propertyName));
            EntityProperty property = properties[propertyName];
            Assert.Equal(expectedType, property.PropertyType);
            Nullable<T> actualValue = actualAccessor.Invoke(property);
            Assert.True(actualValue.HasValue);
            Assert.Equal(expected, actualValue.Value);
        }

        private static void AssertPropertyEqual<T>(T expected,
            EdmType expectedType,
            IDictionary<string, EntityProperty> properties,
            string propertyName,
            Func<EntityProperty, T> actualAccessor)
            where T : class
        {
            Assert.NotNull(properties);
            Assert.True(properties.ContainsKey(propertyName));
            EntityProperty property = properties[propertyName];
            Assert.Equal(expectedType, property.PropertyType);
            T actualValue = actualAccessor.Invoke(property);
            Assert.Equal(expected, actualValue);
        }

        private static void AssertPropertyNull<T>(EdmType expectedType,
            IDictionary<string, EntityProperty> properties,
            string propertyName,
            Func<EntityProperty, Nullable<T>> actualAccessor)
            where T : struct
        {
            Assert.NotNull(properties);
            Assert.True(properties.ContainsKey(propertyName));
            EntityProperty property = properties[propertyName];
            Assert.Equal(expectedType, property.PropertyType);
            Nullable<T> actualValue = actualAccessor.Invoke(property);
            Assert.False(actualValue.HasValue);
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

        private static void RunTrigger(IStorageAccount account, Type programType)
        {
            FunctionalTest.RunTrigger(account, programType);
        }

        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return FunctionalTest.RunTrigger<TResult>(account, programType, setTaskSource);
        }

        private class BindToCloudTableProgram
        {
            public static TaskCompletionSource<CloudTable> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Table(TableName)] CloudTable table)
            {
                TaskSource.TrySetResult(table);
            }
        }

        private class BindToICollectorITableEntityProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage message,
                [Table(TableName)] ICollector<ITableEntity> table)
            {
                Dictionary<string, EntityProperty> properties = new Dictionary<string, EntityProperty>
                {
                    { PropertyName, new EntityProperty(message.AsString) }
                };
                table.Add(new DynamicTableEntity(PartitionKey, RowKey, etag: null, properties: properties));
            }
        }

        private class BindToICollectorPocoProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage message,
                [Table(TableName)] ICollector<Poco> table)
            {
                table.Add(new Poco { PartitionKey = PartitionKey, RowKey = RowKey, Property = message.AsString });
            }
        }

        private class BindToICollectorPocoWithAllTypesProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage message,
                [Table(TableName)] ICollector<PocoWithAllTypes> table)
            {
                PocoWithAllTypes entity = JsonConvert.DeserializeObject<PocoWithAllTypes>(message.AsString);
                table.Add(entity);
            }
        }

        private class BindToIQueryableDynamicTableEntityProgram
        {
            public static TaskCompletionSource<IQueryable<DynamicTableEntity>> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Table(TableName)] IQueryable<DynamicTableEntity> table)
            {
                TaskSource.TrySetResult(table);
            }
        }

        private class Poco
        {
            public string PartitionKey { get; set; }

            public string RowKey { get; set; }

            public string Property { get; set; }
        }

        private class PocoWithAllTypes
        {
            public string PartitionKey { get; set; }

            public string RowKey { get; set; }

            public bool BooleanProperty { get; set; }

            public bool? NullableBooleanProperty { get; set; }

            public byte[] ByteArrayProperty { get; set; }

            public DateTime DateTimeProperty { get; set; }

            public DateTime? NullableDateTimeProperty { get; set; }

            public DateTimeOffset DateTimeOffsetProperty { get; set; }

            public DateTimeOffset? NullableDateTimeOffsetProperty { get; set; }

            public double DoubleProperty { get; set; }

            public double? NullableDoubleProperty { get; set; }

            public Guid GuidProperty { get; set; }

            public Guid? NullableGuidProperty { get; set; }

            public int Int32Property { get; set; }

            public int? NullableInt32Property { get; set; }

            public long Int64Property { get; set; }

            public long? NullableInt64Property { get; set; }

            public string StringProperty { get; set; }

            public Poco PocoProperty { get; set; }
        }
    }
}
