// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Xunit;
using CloudStorageAccount = Microsoft.Azure.Storage.CloudStorageAccount;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

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

        public QueueServiceClient QueueServiceClient { get; private set; }

        public TableServiceClient TableServiceClient { get; private set; }

        public BlobServiceClient BlobServiceClient { get; private set; }

        public QueueClient TestQueue { get; private set; }

        public QueueClient MobileTablesQueue { get; private set; }

        public TableClient TestTable { get; private set; }

        public ScriptHost JobHost { get; private set; }

        public IHost Host { get; private set; }

        public string FixtureId { get; private set; }

        public HttpConfiguration RequestConfiguration { get; }

        public IScriptEventManager EventManager { get; set;  }

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
            QueueServiceClient = new QueueServiceClient(connectionString);
            BlobServiceClient = new BlobServiceClient(connectionString);
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
                    webjobsBuilder.AddAzureStorageBlobs();
                    webjobsBuilder.AddAzureStorageQueues();

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

                    services.AddSingleton<IHostMetrics, HostMetrics>();

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
            var queue = QueueServiceClient.GetQueueClient(string.Format("{0}-{1}", queueName, FixtureId));
            await queue.CreateIfNotExistsAsync();
            await queue.ClearMessagesAsync();
            return queue;
        }

        protected virtual async Task CreateTestStorageEntities()
        {
            TestQueue = QueueServiceClient.GetQueueClient(string.Format("test-input-{0}", FixtureId));
            await TestQueue.CreateIfNotExistsAsync();
            await TestQueue.ClearMessagesAsync();

            MobileTablesQueue = QueueServiceClient.GetQueueClient("mobiletables-input");
            await MobileTablesQueue.CreateIfNotExistsAsync();

            TestInputContainer = BlobServiceClient.GetBlobContainerClient(string.Format("test-input-{0}", FixtureId));
            await TestInputContainer.CreateIfNotExistsAsync();

            TestOutputContainer = BlobServiceClient.GetBlobContainerClient(string.Format("test-output-{0}", FixtureId));
            await TestOutputContainer.CreateIfNotExistsAsync();

            TestTable = TableServiceClient.GetTableClient("test");
            await TestTable.CreateIfNotExistsAsync();
        }

        public async Task DeleteEntities(TableClient table, TableServiceClient tableServiceClient, string partition = null)
        {
            if (!await TableStorageHelpers.TableExistAsync(table, tableServiceClient))
            {
                return;
            }

            string query = string.Empty;
            if (partition != null)
            {
                query = TableClient.CreateQueryFilter($"PartitionKey eq {partition}");
            }

            var entities = table.QueryAsync<TableEntity>(query, null);


            var batch = new List<TableTransactionAction>();
            await foreach (var entity in entities)
            {
                batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity));
            }

            if (batch.Count != 0)
            {
                await table.SubmitTransactionAsync(batch);
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
            public string Name { get; set; }

            public string Region { get; set; }

            public int Status { get; set; }

            public string PartitionKey { get; set; }

            public string RowKey { get; set; }

            public DateTimeOffset? Timestamp { get; set; }

            public ETag ETag { get; set; }
        }
    }
}