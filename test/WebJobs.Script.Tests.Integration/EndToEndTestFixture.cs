// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Web.Http;
using Microsoft.Azure.AppService.Proxy.Client;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.ServiceBus;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Moq;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public abstract class EndToEndTestFixture : IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;

        protected EndToEndTestFixture(string rootPath, string testId, ProxyClientExecutor proxyClient = null)
        {
            _settingsManager = ScriptSettingsManager.Instance;
            FixtureId = testId;
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
                FileLoggingMode = FileLoggingMode.Always
            };

            RequestConfiguration = new HttpConfiguration();
            RequestConfiguration.Formatters.Add(new PlaintextMediaTypeFormatter());

            EventManager = new ScriptEventManager();
            ScriptHostEnvironmentMock = new Mock<IScriptHostEnvironment>();

            // Reset the timer logs first, since one of the tests will
            // be checking them
            TestHelpers.ClearFunctionLogs("TimerTrigger");
            TestHelpers.ClearFunctionLogs("ListenerStartupException");

            InitializeConfig(config);
            Func<string, FunctionDescriptor> funcLookup = (name) => this.Host.GetFunctionOrNull(name);
            var fastLogger = new FunctionInstanceLogger(funcLookup, new MetricsLogger());
            config.HostConfig.AddService<IAsyncCollector<FunctionInstanceLogEntry>>(fastLogger);

            Host = ScriptHost.Create(ScriptHostEnvironmentMock.Object, EventManager, config, _settingsManager, proxyClient);
            Host.Start();
        }

        public Mock<IScriptHostEnvironment> ScriptHostEnvironmentMock { get; }

        public TestTraceWriter TraceWriter { get; private set; }

        public CloudBlobContainer TestInputContainer { get; private set; }

        public CloudBlobContainer TestOutputContainer { get; private set; }

        public CloudQueueClient QueueClient { get; private set; }

        public CloudTableClient TableClient { get; private set; }

        public CloudBlobClient BlobClient { get; private set; }

        public Microsoft.ServiceBus.Messaging.QueueClient ServiceBusQueueClient { get; private set; }

        public NamespaceManager NamespaceManager { get; private set; }

        public CloudQueue TestQueue { get; private set; }

        public CloudQueue MobileTablesQueue { get; private set; }

        public CloudTable TestTable { get; private set; }

        public ScriptHost Host { get; private set; }

        public string FixtureId { get; private set; }

        public HttpConfiguration RequestConfiguration { get; }

        public IScriptEventManager EventManager { get; }

        protected virtual void InitializeConfig(ScriptHostConfiguration config)
        {
        }

        public CloudQueue GetNewQueue(string queueName)
        {
            var queue = QueueClient.GetQueueReference(string.Format("{0}-{1}", queueName, FixtureId));
            queue.CreateIfNotExists();
            queue.Clear();
            return queue;
        }

        protected virtual void CreateTestStorageEntities()
        {
            TestQueue = QueueClient.GetQueueReference(string.Format("test-input-{0}", FixtureId));
            TestQueue.CreateIfNotExists();
            TestQueue.Clear();

            // This queue name should really be suffixed by -fsharp, -csharp, -node etc.
            MobileTablesQueue = QueueClient.GetQueueReference("mobiletables-input");
            MobileTablesQueue.CreateIfNotExists(); // do not clear this queue since it is currently shared between fixtures

            TestInputContainer = BlobClient.GetContainerReference(string.Format("test-input-{0}", FixtureId));
            TestInputContainer.CreateIfNotExists();

            // Processing a large number of blobs on startup can take a while,
            // so let's start with an empty container.
            TestHelpers.ClearContainer(TestInputContainer);

            TestOutputContainer = BlobClient.GetContainerReference(string.Format("test-output-{0}", FixtureId));
            TestOutputContainer.CreateIfNotExists();
            TestHelpers.ClearContainer(TestOutputContainer);

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

            string serviceBusQueueName = string.Format("test-input-{0}", FixtureId);
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);
            NamespaceManager = NamespaceManager.CreateFromConnectionString(connectionString);

            NamespaceManager.DeleteQueue(serviceBusQueueName);
            NamespaceManager.CreateQueue(serviceBusQueueName);

            ServiceBusQueueClient = Microsoft.ServiceBus.Messaging.QueueClient.CreateFromConnectionString(connectionString, serviceBusQueueName);
        }

        public virtual void Dispose()
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
