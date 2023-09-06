// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using System.Runtime.InteropServices;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Data.Tables;
using System.Data;
using Azure;

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
        private readonly bool _addWorkerConcurrency;
        private readonly TimeSpan? _addWorkerDelay;

        protected ScriptHostEndToEndTestFixture(string rootPath, string testId, string functionsWorkerLanguage, ProxyClientExecutor proxyClient = null,
            bool startHost = true, ICollection<string> functions = null, bool addWorkerConcurrency = false, TimeSpan? addWorkerDelay = null)
        {
            _settingsManager = ScriptSettingsManager.Instance;
            FixtureId = testId;
            RequestConfiguration = new HttpConfiguration();
            EventManager = new ScriptEventManager();
            MockApplicationLifetime = new Mock<IApplicationLifetime>();
            LoggerProvider = new TestLoggerProvider();

            _rootPath = rootPath;
            _proxyClient = proxyClient;
            _startHost = startHost;
            _functions = functions;
            _functionsWorkerLanguage = functionsWorkerLanguage;
            _addWorkerConcurrency = addWorkerConcurrency;
            _addWorkerDelay = addWorkerDelay;
        }

        public TestLoggerProvider LoggerProvider { get; }

        public Mock<IApplicationLifetime> MockApplicationLifetime { get; }

        public BlobContainerClient TestInputContainer { get; private set; }

        public BlobContainerClient TestOutputContainer { get; private set; }

        public QueueServiceClient QueueClient { get; private set; }

        public TableServiceClient TableServiceClient { get; private set; }

        public BlobServiceClient BlobClient { get; private set; }

        public QueueClient TestQueue { get; private set; }

        public QueueClient MobileTablesQueue { get; private set; }

        public TableClient TestTableClient { get; private set; }

        public ScriptHost JobHost { get; private set; }

        public IHost Host { get; private set; }

        public string FixtureId { get; private set; }

        public HttpConfiguration RequestConfiguration { get; }

        public IScriptEventManager EventManager { get; set; }

        public async Task InitializeAsync()
        {
            if (!string.IsNullOrEmpty(_functionsWorkerLanguage))
            {
                Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, _functionsWorkerLanguage);
            }
            if (_addWorkerConcurrency)
            {
                Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled, "true");
            }
            IConfiguration configuration = TestHelpers.GetTestConfiguration();
            string connectionString = configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
            QueueClient = new QueueServiceClient(connectionString);
            BlobClient = new BlobServiceClient(connectionString);
            TableServiceClient = new TableServiceClient(connectionString);

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
                   webjobsBuilder.AddAzureStorageCoreServices();

                   // This needs to added manually at the ScriptHost level, as although FunctionMetadataManager is available through WebHost,
                   // it needs to change the services during its lifetime.
                   webjobsBuilder.Services.AddSingleton<IFunctionMetadataManager, FunctionMetadataManager>();
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

                   // Shared memory data transfer
                   if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                   {
                       services.AddSingleton<IMemoryMappedFileAccessor, MemoryMappedFileAccessorWindows>();
                   }
                   else
                   {
                       services.AddSingleton<IMemoryMappedFileAccessor, MemoryMappedFileAccessorUnix>();
                   }
                   services.AddSingleton<ISharedMemoryManager, SharedMemoryManager>();
                   if (_addWorkerConcurrency && _addWorkerDelay > TimeSpan.Zero)
                   {
                       services.AddSingleton<IScriptEventManager>(new WorkerConcurrencyManagerEndToEndTests.TestScriptEventManager(_addWorkerDelay.Value));
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

        public async Task<QueueClient> GetNewQueue(string queueName)
        {
            QueueClient queueClient = QueueClient.GetQueueClient($"{queueName}-{FixtureId}");
            await queueClient.CreateIfNotExistsAsync();
            await queueClient.ClearMessagesAsync();

            return queueClient;
        }

        protected virtual async Task CreateTestStorageEntities()
        {
            TestQueue = await GetNewQueue("test-input");

            // This queue name should really be suffixed by -fsharp, -csharp, -node etc.
            MobileTablesQueue = QueueClient.GetQueueClient("mobiletables-input");
            await MobileTablesQueue.CreateIfNotExistsAsync(); // do not clear this queue since it is currently shared between fixtures

            TestInputContainer = BlobClient.GetBlobContainerClient($"test-input-{FixtureId}");
            await TestInputContainer.CreateIfNotExistsAsync();

            // Processing a large number of blobs on startup can take a while,
            // so let's start with an empty container.
            await TestHelpers.ClearContainerAsync(TestInputContainer);

            TestOutputContainer = BlobClient.GetBlobContainerClient($"test-output-{FixtureId}");
            await TestOutputContainer.CreateIfNotExistsAsync();
            await TestHelpers.ClearContainerAsync(TestOutputContainer);

            TestTableClient = TableServiceClient.GetTableClient("test");
            await TestTableClient.CreateIfNotExistsAsync();

            await DeleteEntities(TestTableClient, "AAA");
            await DeleteEntities(TestTableClient, "BBB");

            List<TableTransactionAction> batch = new List<TableTransactionAction>()
            {
                new TableTransactionAction(TableTransactionActionType.Add, new TestEntity { PartitionKey = "AAA", RowKey = "001", Region = "West", Name = "Test Entity 1", Status = 0 }),
                new TableTransactionAction(TableTransactionActionType.Add, new TestEntity { PartitionKey = "AAA", RowKey = "002", Region = "East", Name = "Test Entity 2", Status = 1 }),
                new TableTransactionAction(TableTransactionActionType.Add, new TestEntity { PartitionKey = "AAA", RowKey = "003", Region = "West", Name = "Test Entity 3", Status = 1 }),
                new TableTransactionAction(TableTransactionActionType.Add, new TestEntity { PartitionKey = "AAA", RowKey = "004", Region = "West", Name = "Test Entity 4", Status = 1 }),
                new TableTransactionAction(TableTransactionActionType.Add, new TestEntity { PartitionKey = "AAA", RowKey = "005", Region = "East", Name = "Test Entity 5", Status = 0 }),
            };
            await TestTableClient.SubmitTransactionAsync(batch).ConfigureAwait(false);

            batch = new List<TableTransactionAction>()
            {
                new TableTransactionAction(TableTransactionActionType.Add, new TestEntity { PartitionKey = "BBB", RowKey = "001", Region = "South", Name = "Test Entity 1", Status = 0 }),
                new TableTransactionAction(TableTransactionActionType.Add, new TestEntity { PartitionKey = "BBB", RowKey = "002", Region = "West", Name = "Test Entity 2", Status = 1 }),
                new TableTransactionAction(TableTransactionActionType.Add, new TestEntity { PartitionKey = "BBB", RowKey = "003", Region = "West", Name = "Test Entity 3", Status = 0 }),
            };
            await TestTableClient.SubmitTransactionAsync(batch).ConfigureAwait(false);
        }

        public async Task DeleteEntities(TableClient tableClient, string partition = null)
        {
            string query = string.IsNullOrEmpty(partition) ? string.Empty : $"PartitionKey eq '{partition}'";

            List<TableTransactionAction> batch = new List<TableTransactionAction>();

            AsyncPageable<TableEntity> tableEntities = tableClient.QueryAsync<TableEntity>(query);
            await foreach (TableEntity entity in tableEntities)
            {
                batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity));
            }

            if (batch.Any())
            {
                await tableClient.SubmitTransactionAsync(batch).ConfigureAwait(false);
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
                Host.Dispose();
            }
            Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, string.Empty);
        }

        private class TestEntity : ITableEntity
        {
            public string PartitionKey { get; set; }

            public string RowKey { get; set; }

            public DateTimeOffset? Timestamp { get; set; }

            public ETag ETag { get; set; }

            public string Name { get; set; }

            public string Region { get; set; }

            public int Status { get; set; }
        }
    }
}