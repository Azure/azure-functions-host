// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Azure.WebJobs.Script.BindingExtensions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using CloudStorageAccount = Microsoft.Azure.Storage.CloudStorageAccount;
using TableStorageAccount = Microsoft.Azure.Cosmos.Table.CloudStorageAccount;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public abstract class EndToEndTestFixture : IAsyncLifetime
    {
        private readonly string _rootPath;
        private string _copiedRootPath;
        private string _functionsWorkerRuntime;
        private int _workerProcessCount;
        private string _functionsWorkerRuntimeVersion;
        private bool _addTestSettings;

        protected EndToEndTestFixture(string rootPath, string testId,
            string functionsWorkerRuntime,
            int workerProcessesCount = 1,
            string functionsWorkerRuntimeVersion = null,
            bool addTestSettings = true)
        {
            FixtureId = testId;

            _rootPath = rootPath;
            _functionsWorkerRuntime = functionsWorkerRuntime;
            _workerProcessCount = workerProcessesCount;
            _functionsWorkerRuntimeVersion = functionsWorkerRuntimeVersion;
            _addTestSettings = addTestSettings;
        }

        public CloudBlobContainer TestInputContainer { get; private set; }

        public CloudBlobContainer TestOutputContainer { get; private set; }

        public CloudQueueClient QueueClient { get; private set; }

        public CloudTableClient TableClient { get; private set; }

        public CloudBlobClient BlobClient { get; private set; }

        public CloudQueue TestQueue { get; private set; }

        public CloudQueue MobileTablesQueue { get; private set; }

        public CloudTable TestTable { get; private set; }

        public TestFunctionHost Host { get; private set; }

        public string FixtureId { get; private set; }

        public TestMetricsLogger MetricsLogger { get; private set; } = new TestMetricsLogger();

        public Mock<IFunctionsSyncManager> FunctionsSyncManagerMock { get; private set; }

        public TestEventGenerator EventGenerator { get; private set; } = new TestEventGenerator();

        public string HostInstanceId => Host.JobHostServices.GetService<IOptions<ScriptJobHostOptions>>().Value.InstanceId;

        public string MasterKey { get; private set; }

        public string RootScriptPath { get; private set; }

        protected virtual ExtensionPackageReference[] GetExtensionsToInstall()
        {
            return null;
        }

        public async Task InitializeAsync()
        {
            string nowString = DateTime.UtcNow.ToString("yyMMdd-HHmmss");
            string GetDestPath(int counter)
            {
                return Path.Combine(Path.GetTempPath(), "FunctionsE2E", $"{nowString}_{counter}");
            }

            // Prevent collisions.
            int i = 0;
            _copiedRootPath = GetDestPath(i++);
            while (Directory.Exists(_copiedRootPath))
            {
                _copiedRootPath = GetDestPath(i++);
            }

            FileUtility.CopyDirectory(_rootPath, _copiedRootPath);

            var extensionsToInstall = GetExtensionsToInstall();
            if (extensionsToInstall != null && extensionsToInstall.Length > 0)
            {
                TestFunctionHost.WriteNugetPackageSources(_copiedRootPath, "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsTempStaging/nuget/v3/index.json", "https://api.nuget.org/v3/index.json");
                var options = new OptionsWrapper<ScriptJobHostOptions>(new ScriptJobHostOptions
                {
                    RootScriptPath = _copiedRootPath
                });

                var manager = new ExtensionsManager(options, NullLogger<ExtensionsManager>.Instance, new TestExtensionBundleManager());
                await manager.AddExtensions(extensionsToInstall);
            }

            string logPath = Path.Combine(Path.GetTempPath(), @"Functions");
            if (!string.IsNullOrEmpty(_functionsWorkerRuntime))
            {
                Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, _functionsWorkerRuntime);
                Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName, _workerProcessCount.ToString());
            }
            if (!string.IsNullOrEmpty(_functionsWorkerRuntimeVersion))
            {
                Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName, _functionsWorkerRuntimeVersion);
            }

            FunctionsSyncManagerMock = new Mock<IFunctionsSyncManager>(MockBehavior.Strict);
            FunctionsSyncManagerMock.Setup(p => p.TrySyncTriggersAsync(It.IsAny<bool>())).ReturnsAsync(new SyncTriggersResult { Success = true });

            Host = new TestFunctionHost(_copiedRootPath, logPath, addTestSettings: _addTestSettings,
                configureScriptHostWebJobsBuilder: webJobsBuilder =>
                {
                    ConfigureScriptHost(webJobsBuilder);
                },
                configureScriptHostServices: s =>
                {
                    s.AddSingleton<IFunctionsSyncManager>(_ => FunctionsSyncManagerMock.Object);
                    s.AddSingleton<IMetricsLogger>(_ => MetricsLogger);
                    ConfigureScriptHost(s);
                },
                configureScriptHostAppConfiguration: configBuilder =>
                {
                    ConfigureScriptHost(configBuilder);
                },
                configureWebHostServices: s =>
                {
                    s.AddSingleton<IEventGenerator>(_ => EventGenerator);
                    ConfigureWebHost(s);
                },
                configureWebHostAppConfiguration: configBuilder =>
                {
                    ConfigureWebHost(configBuilder);
                });

            string connectionString = Host.JobHostServices?.GetService<IConfiguration>().GetWebJobsConnectionString(ConnectionStringNames.Storage);
            if (!string.IsNullOrEmpty(connectionString))
            {
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);

                QueueClient = storageAccount.CreateCloudQueueClient();
                BlobClient = storageAccount.CreateCloudBlobClient();

                TableStorageAccount tableStorageAccount = TableStorageAccount.Parse(connectionString);
                TableClient = tableStorageAccount.CreateCloudTableClient();

                await CreateTestStorageEntities();
            }

            MasterKey = await Host.GetMasterKeyAsync();
            RootScriptPath = _copiedRootPath;
        }

        public virtual void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
        }

        public virtual void ConfigureScriptHost(IServiceCollection services)
        {
        }

        public virtual void ConfigureScriptHost(IConfigurationBuilder configBuilder)
        {
        }

        public virtual void ConfigureWebHost(IServiceCollection services)
        {
        }

        public virtual void ConfigureWebHost(IConfigurationBuilder configBuilder)
        {
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

        public virtual Task DisposeAsync()
        {
            Host?.Dispose();

            // Clean up all but the last 5 directories for debugging failures.
            var directoriesToDelete = Directory.EnumerateDirectories(Path.GetDirectoryName(_copiedRootPath))
                .OrderByDescending(p => p)
                .Skip(5);

            foreach (string directory in directoriesToDelete)
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch
                {
                    // best effort
                }
            }
            Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, string.Empty);
            Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName, string.Empty);
            Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName, string.Empty);
            return Task.CompletedTask;
        }

        public void AssertNoScriptHostErrors()
        {
            var logs = Host.GetScriptHostLogMessages();
            var errors = logs.Where(x => x.Level == Microsoft.Extensions.Logging.LogLevel.Error).ToList();
            if (errors.Count > 0)
            {
                var messageBuilder = new StringBuilder();

                foreach (var e in errors)
                    messageBuilder.AppendLine(e.FormattedMessage);

                Assert.True(errors.Count == 0, messageBuilder.ToString());
            }
        }

        public void AssertScriptHostErrors()
        {
            var logs = Host.GetScriptHostLogMessages();
            var errors = logs.Where(x => x.Level == Microsoft.Extensions.Logging.LogLevel.Error).ToList();
            Assert.True(errors.Count > 0);
        }

        public async Task AddMasterKey(HttpRequestMessage request)
        {
            var masterKey = await Host.GetMasterKeyAsync();
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, masterKey);
        }

        private class TestExtensionBundleManager : IExtensionBundleManager
        {
            public Task<string> GetExtensionBundleBinPathAsync() => Task.FromResult<string>(null);
            public Task<ExtensionBundleDetails> GetExtensionBundleDetails() => Task.FromResult<ExtensionBundleDetails>(null);

            public Task<string> GetExtensionBundlePath(HttpClient httpClient = null) => Task.FromResult<string>(null);

            public Task<string> GetExtensionBundlePath() => Task.FromResult<string>(null);

            public bool IsExtensionBundleConfigured() => false;

            public bool IsLegacyExtensionBundle() => false;

        }

        private class TestEntity : TableEntity
        {
            public string Name { get; set; }

            public string Region { get; set; }

            public int Status { get; set; }
        }
    }
}
