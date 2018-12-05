﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public abstract class ScriptHostEndToEndTestFixture : IAsyncLifetime
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly ManualResetEventSlim _hostStartedEvent = new ManualResetEventSlim();
        private readonly string _rootPath;
        private readonly ProxyClientExecutor _proxyClient;
        private readonly bool _startHost;
        private readonly ICollection<string> _functions;
        private readonly string _functionsWorkerLanguage;

        protected ScriptHostEndToEndTestFixture(string rootPath, string testId, string functionsWorkerLanguage, ProxyClientExecutor proxyClient = null,
            bool startHost = true, ICollection<string> functions = null)
        {
            _settingsManager = ScriptSettingsManager.Instance;
            FixtureId = testId;
            RequestConfiguration = new HttpConfiguration();
            EventManager = new ScriptEventManager();
            ScriptJobHostEnvironmentMock = new Mock<IScriptJobHostEnvironment>();
            LoggerProvider = new TestLoggerProvider();

            _rootPath = rootPath;
            _proxyClient = proxyClient;
            _startHost = startHost;
            _functions = functions;
            _functionsWorkerLanguage = functionsWorkerLanguage;
        }

        public TestLoggerProvider LoggerProvider { get; }

        public Mock<IScriptJobHostEnvironment> ScriptJobHostEnvironmentMock { get; }

        public CloudBlobContainer TestInputContainer { get; private set; }

        public CloudBlobContainer TestOutputContainer { get; private set; }

        public CloudQueueClient QueueClient { get; private set; }

        public CloudTableClient TableClient { get; private set; }

        public CloudBlobClient BlobClient { get; private set; }

        public CloudQueue TestQueue { get; private set; }

        public CloudQueue MobileTablesQueue { get; private set; }

        public CloudTable TestTable { get; private set; }

        public ScriptHost JobHost { get; private set; }

        public IHost Host { get; private set; }

        public string FixtureId { get; private set; }

        public HttpConfiguration RequestConfiguration { get; }

        public IScriptEventManager EventManager { get; }

        public async Task InitializeAsync()
        {
            if (!string.IsNullOrEmpty(_functionsWorkerLanguage))
            {
                Environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, _functionsWorkerLanguage);
            }
            IConfiguration configuration = TestHelpers.GetTestConfiguration();
            string connectionString = configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            QueueClient = storageAccount.CreateCloudQueueClient();
            BlobClient = storageAccount.CreateCloudBlobClient();
            TableClient = storageAccount.CreateCloudTableClient();

            await CreateTestStorageEntities();

            // ApiHubTestHelper.SetDefaultConnectionFactory();

            //ILoggerProviderFactory loggerProviderFactory = new TestLoggerProviderFactory(LoggerProvider);

            // Reset the timer logs first, since one of the tests will
            // be checking them
            TestHelpers.ClearFunctionLogs("TimerTrigger");
            TestHelpers.ClearFunctionLogs("ListenerStartupException");

             Host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(webjobsBuilder =>
                {
                    webjobsBuilder.AddAzureStorage();
                },                
                o =>
                {
                    o.ScriptPath = _rootPath;
                    o.LogPath = TestHelpers.GetHostLogFileDirectory().Parent.FullName;
                },
                runStartupHostedServices: true)
                .ConfigureServices(services =>
                {
                    services.Configure<ScriptJobHostOptions>(o =>
                    {
                        o.FileLoggingMode = FileLoggingMode.Always;

                        if (_functions != null)
                        {
                            o.Functions = _functions;
                        }
                    });

                    if (_proxyClient != null)
                    {
                        services.AddSingleton<ProxyClientExecutor>(_proxyClient);
                    }

                    ConfigureServices(services);
                })
                .ConfigureLogging(b =>
                {
                    b.AddProvider(LoggerProvider);
                })
                .Build();

            JobHost = Host.GetScriptHost();

            if (_startHost)
            {
                JobHost.HostStarted += (s, e) => _hostStartedEvent.Set();
                await Host.StartAsync();
                _hostStartedEvent.Wait(TimeSpan.FromSeconds(30));
            }
        }

        public async Task<CloudQueue> GetNewQueue(string queueName)
        {
            var queue = QueueClient.GetQueueReference(string.Format("{0}-{1}", queueName, FixtureId));
            await queue.CreateIfNotExistsAsync();
            await queue.ClearAsync();
            return queue;
        }

        protected virtual async Task CreateTestStorageEntities()
        {
            TestQueue = QueueClient.GetQueueReference(string.Format("test-input-{0}", FixtureId));
            await TestQueue.CreateIfNotExistsAsync();
            await TestQueue.ClearAsync();

            // This queue name should really be suffixed by -fsharp, -csharp, -node etc.
            MobileTablesQueue = QueueClient.GetQueueReference("mobiletables-input");
            await MobileTablesQueue.CreateIfNotExistsAsync(); // do not clear this queue since it is currently shared between fixtures

            TestInputContainer = BlobClient.GetContainerReference(string.Format("test-input-{0}", FixtureId));
            await TestInputContainer.CreateIfNotExistsAsync();

            // Processing a large number of blobs on startup can take a while,
            // so let's start with an empty container.
            await TestHelpers.ClearContainerAsync(TestInputContainer);

            TestOutputContainer = BlobClient.GetContainerReference(string.Format("test-output-{0}", FixtureId));
            await TestOutputContainer.CreateIfNotExistsAsync();
            await TestHelpers.ClearContainerAsync(TestOutputContainer);

            TestTable = TableClient.GetTableReference("test");
            await TestTable.CreateIfNotExistsAsync();

            await DeleteEntities(TestTable, "AAA");
            await DeleteEntities(TestTable, "BBB");

            var batch = new TableBatchOperation();
            batch.Insert(new TestEntity { PartitionKey = "AAA", RowKey = "001", Region = "West", Name = "Test Entity 1", Status = 0 });
            batch.Insert(new TestEntity { PartitionKey = "AAA", RowKey = "002", Region = "East", Name = "Test Entity 2", Status = 1 });
            batch.Insert(new TestEntity { PartitionKey = "AAA", RowKey = "003", Region = "West", Name = "Test Entity 3", Status = 1 });
            batch.Insert(new TestEntity { PartitionKey = "AAA", RowKey = "004", Region = "West", Name = "Test Entity 4", Status = 1 });
            batch.Insert(new TestEntity { PartitionKey = "AAA", RowKey = "005", Region = "East", Name = "Test Entity 5", Status = 0 });
            await TestTable.ExecuteBatchAsync(batch);

            batch = new TableBatchOperation();
            batch.Insert(new TestEntity { PartitionKey = "BBB", RowKey = "001", Region = "South", Name = "Test Entity 1", Status = 0 });
            batch.Insert(new TestEntity { PartitionKey = "BBB", RowKey = "002", Region = "West", Name = "Test Entity 2", Status = 1 });
            batch.Insert(new TestEntity { PartitionKey = "BBB", RowKey = "003", Region = "West", Name = "Test Entity 3", Status = 0 });
            await TestTable.ExecuteBatchAsync(batch);
        }

        public async Task DeleteEntities(CloudTable table, string partition = null)
        {
            if (!await table.ExistsAsync())
            {
                return;
            }

            TableQuery query = new TableQuery();
            if (partition != null)
            {
                query.FilterString = string.Format("PartitionKey eq '{0}'", partition);
            }

            var entities = await table.ExecuteQuerySegmentedAsync(query, null);

            if (entities.Any())
            {
                var batch = new TableBatchOperation();
                foreach (var entity in entities)
                {
                    batch.Delete(entity);
                }
                await table.ExecuteBatchAsync(batch);
            }
        }

        public virtual void ConfigureServices(IServiceCollection services)
        {
        }

        public virtual async Task DisposeAsync()
        {
            if (JobHost != null)
            {
                await JobHost.StopAsync();
                await Host.StopAsync();
                JobHost.Dispose();
            }
            Environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, string.Empty);
        }

        private class TestEntity : TableEntity
        {
            public string Name { get; set; }

            public string Region { get; set; }

            public int Status { get; set; }
        }
    }
}