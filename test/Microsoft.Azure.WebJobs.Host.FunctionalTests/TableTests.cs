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

        private class BindToIQueryableDynamicTableEntityProgram
        {
            public static TaskCompletionSource<IQueryable<DynamicTableEntity>> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Table(TableName)] IQueryable<DynamicTableEntity> table)
            {
                TaskSource.TrySetResult(table);
            }
        }
    }
}
