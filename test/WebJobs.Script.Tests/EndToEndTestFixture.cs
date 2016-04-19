// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public abstract class EndToEndTestFixture : IDisposable
    {
        private CloudQueueClient _queueClient;
        private CloudBlobClient _blobClient;
        private CloudTableClient _tableClient;

        protected EndToEndTestFixture(string rootPath)
        {
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            _queueClient = storageAccount.CreateCloudQueueClient();
            _blobClient = storageAccount.CreateCloudBlobClient();
            _tableClient = storageAccount.CreateCloudTableClient();

            CreateTestStorageEntities();
            TraceWriter = new TestTraceWriter(TraceLevel.Verbose);

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath,
                TraceWriter = TraceWriter,
                FileLoggingEnabled = true
            };

            HostManager = new ScriptHostManager(config);

            Thread t = new Thread(_ =>
            {
                HostManager.RunAndBlock();
            });
            t.Start();

            TestHelpers.Await(() => HostManager.IsRunning).Wait();
        }

        public TestTraceWriter TraceWriter { get; private set; }

        public CloudBlobContainer TestContainer { get; private set; }

        public CloudQueue TestQueue { get; private set; }

        public CloudTable TestTable { get; private set; }

        public ScriptHost Host
        {
            get { return HostManager.Instance; }
        }

        public ScriptHostManager HostManager { get; private set; }

        public CloudQueue GetNewQueue(string queueName)
        {
            var queue = _queueClient.GetQueueReference(queueName);
            queue.CreateIfNotExists();
            queue.Clear();
            return queue;
        }

        private void CreateTestStorageEntities()
        {
            TestQueue = _queueClient.GetQueueReference("test-input");
            TestQueue.CreateIfNotExists();
            TestQueue.Clear();

            TestContainer = _blobClient.GetContainerReference("test-output");
            TestContainer.CreateIfNotExists();

            TestTable = _tableClient.GetTableReference("test");
            TestTable.CreateIfNotExists();

            DeleteEntities("AAA");
            DeleteEntities("BBB");

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
        }

        public void Dispose()
        {
            HostManager.Stop();
            HostManager.Dispose();
        }

        private void DeleteEntities(string partition)
        {
            var batch = new TableBatchOperation();
            var query = TestTable.CreateQuery<TestEntity>();
            var entities = query.Execute().Where(p => p.PartitionKey == partition);
            if (entities.Any())
            {
                foreach (var entity in entities)
                {
                    batch.Delete(entity);
                }
                TestTable.ExecuteBatch(batch);
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
