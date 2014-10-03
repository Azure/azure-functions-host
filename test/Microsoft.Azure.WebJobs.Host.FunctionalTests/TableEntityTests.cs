// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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

        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return FunctionalTest.RunTrigger<TResult>(account, programType, setTaskSource);
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
    }
}
