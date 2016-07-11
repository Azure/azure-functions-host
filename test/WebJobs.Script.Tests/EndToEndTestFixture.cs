// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Tests.ApiHub;
using Microsoft.ServiceBus;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public abstract class EndToEndTestFixture : IDisposable
    {
        private string _testId;

        protected EndToEndTestFixture(string rootPath, string testId)
        {
            _testId = testId;
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            QueueClient = storageAccount.CreateCloudQueueClient();
            BlobClient = storageAccount.CreateCloudBlobClient();
            TableClient = storageAccount.CreateCloudTableClient();

            CreateTestStorageEntities();
            TraceWriter = new TestTraceWriter(TraceLevel.Verbose);

            ApiHubTestHelper.SetDefaultConnectionFactory();

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath,
                TraceWriter = TraceWriter,
                FileLoggingEnabled = true
            };

            Host = ScriptHost.Create(config);
            Host.Start();
        }

        public TestTraceWriter TraceWriter { get; private set; }

        public CloudBlobContainer TestInputContainer { get; private set; }

        public CloudBlobContainer TestOutputContainer { get; private set; }

        public CloudQueueClient QueueClient { get; private set; }

        public CloudTableClient TableClient { get; private set; }

        public CloudBlobClient BlobClient { get; private set; }

        public Microsoft.ServiceBus.Messaging.QueueClient ServiceBusQueueClient { get; private set; }

        public CloudQueue TestQueue { get; private set; }

        public CloudTable TestTable { get; private set; }

        public ScriptHost Host { get; private set; }

        public CloudQueue GetNewQueue(string queueName)
        {
            var queue = QueueClient.GetQueueReference(queueName);
            queue.CreateIfNotExists();
            queue.Clear();
            return queue;
        }

        protected virtual void CreateTestStorageEntities()
        {
            TestQueue = QueueClient.GetQueueReference(string.Format("test-input-{0}", _testId));
            TestQueue.CreateIfNotExists();
            TestQueue.Clear();

            TestInputContainer = BlobClient.GetContainerReference(string.Format("test-input-{0}", _testId));
            TestInputContainer.CreateIfNotExists();

            TestOutputContainer = BlobClient.GetContainerReference(string.Format("test-output-{0}", _testId));
            TestOutputContainer.CreateIfNotExists();

            TestTable = TableClient.GetTableReference("test");
            TestTable.CreateIfNotExists();

            DeleteEntities(TestTable, "AAA");
            DeleteEntities(TestTable, "BBB");

            var batch = new TableBatchOperation();
            batch.Insert(new TestEntity { PartitionKey = "AAA", RowKey = "001", Region = "West", Name = "Test Entity 1", Status = 0 });
            batch.Insert(new TestEntity { PartitionKey = "AAA", RowKey = "002", Region = "East", Name = "Test Entity 2", Status = 1 });
            batch.Insert(new TestEntity { PartitionKey = "AAA", RowKey = "003", Region = "West", Name = "Test Entity 3", Status = 1 });
            batch.Insert(new TestEntity { PartitionKey = "AAA", RowKey = "004", Region = "West", Name = "Test Entity 4", Status = 1 });
            batch.Insert(new TestEntity { PartitionKey = "AAA", RowKey = "005", Region = "East", Name = "Test Entity 5", Status = 0 });
            TestTable.ExecuteBatch(batch);

            batch = new TableBatchOperation();
            batch.Insert(new TestEntity { PartitionKey = "BBB", RowKey = "001", Region = "South", Name = "Test Entity 1", Status = 0 });
            batch.Insert(new TestEntity { PartitionKey = "BBB", RowKey = "002", Region = "West", Name = "Test Entity 2", Status = 1 });
            batch.Insert(new TestEntity { PartitionKey = "BBB", RowKey = "003", Region = "West", Name = "Test Entity 3", Status = 0 });
            TestTable.ExecuteBatch(batch);

            string serviceBusQueueName = string.Format("test-input-{0}", _testId);
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);
            var namespaceManager = NamespaceManager.CreateFromConnectionString(connectionString);

            namespaceManager.DeleteQueue(serviceBusQueueName);
            namespaceManager.CreateQueue(serviceBusQueueName);

            ServiceBusQueueClient = Microsoft.ServiceBus.Messaging.QueueClient.CreateFromConnectionString(connectionString, serviceBusQueueName);
        }

        public void Dispose()
        {
            Host.Stop();
            Host.Dispose();
            ServiceBusQueueClient.Close();
        }

        public void DeleteEntities(CloudTable table, string partition = null)
        {
            if (!table.Exists())
            {
                return;
            }

            TableQuery query = new TableQuery();
            if (partition != null)
            {
                query.FilterString = string.Format("PartitionKey eq '{0}'", partition);
            }

            var entities = table.ExecuteQuery(query);

            if (entities.Any())
            {
                var batch = new TableBatchOperation();
                foreach (var entity in entities)
                {
                    batch.Delete(entity);
                }
                table.ExecuteBatch(batch);
            }
        }

        private class TestEntity : TableEntity
        {
            public string Name { get; set; }
            public string Region { get; set; }
            public int Status { get; set; }
        }
    }
}
