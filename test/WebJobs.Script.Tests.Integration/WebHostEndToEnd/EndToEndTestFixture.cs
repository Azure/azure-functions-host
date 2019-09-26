// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.BindingExtensions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public abstract class EndToEndTestFixture : IAsyncLifetime
    {
        private readonly string _rootPath;
        private string _copiedRootPath;
        private string _functionsWorkerRuntime;
        private int _workerProcessCount;

        protected EndToEndTestFixture(string rootPath, string testId, string functionsWorkerRuntime, int workerProcessesCount = 1)
        {
            FixtureId = testId;

            _rootPath = rootPath;
            _functionsWorkerRuntime = functionsWorkerRuntime;
            _workerProcessCount = workerProcessesCount;

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

        protected virtual ExtensionPackageReference[] GetExtensionsToInstall()
        {
            return null;
        }

        public async Task InitializeAsync()
        {
            _copiedRootPath = Path.Combine(Path.GetTempPath(), "FunctionsE2E", DateTime.UtcNow.ToString("yyMMdd-HHmmss"));
            FileUtility.CopyDirectory(_rootPath, _copiedRootPath);

            var extensionsToInstall = GetExtensionsToInstall();
            if (extensionsToInstall != null && extensionsToInstall.Length > 0)
            {
                TestFunctionHost.WriteNugetPackageSources(_copiedRootPath, "http://www.myget.org/F/azure-appservice/api/v2", "https://www.myget.org/F/azure-appservice-staging/api/v2", "https://api.nuget.org/v3/index.json");
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
                Environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, _functionsWorkerRuntime);
                Environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionsWorkerProcessCountSettingName, _workerProcessCount.ToString());
            }

            FunctionsSyncManagerMock = new Mock<IFunctionsSyncManager>(MockBehavior.Strict);
            FunctionsSyncManagerMock.Setup(p => p.TrySyncTriggersAsync(It.IsAny<bool>())).ReturnsAsync(new SyncTriggersResult { Success = true });

            Host = new TestFunctionHost(_copiedRootPath, logPath,
                configureScriptHostWebJobsBuilder: webJobsBuilder =>
                {
                    ConfigureScriptHost(webJobsBuilder);
                },
                configureScriptHostServices: s =>
                {
                    s.AddSingleton<IFunctionsSyncManager>(_ => FunctionsSyncManagerMock.Object);
                    s.AddSingleton<IMetricsLogger>(_ => MetricsLogger);
                },
                configureWebHostServices: s =>
                {
                    s.AddSingleton<IEventGenerator>(_ => EventGenerator);
                });

            string connectionString = Host.JobHostServices.GetService<IConfiguration>().GetWebJobsConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);

            QueueClient = storageAccount.CreateCloudQueueClient();
            BlobClient = storageAccount.CreateCloudBlobClient();
            TableClient = storageAccount.CreateCloudTableClient();

            await CreateTestStorageEntities();
        }

        public virtual void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
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
            Environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, string.Empty);
            Environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionsWorkerProcessCountSettingName, string.Empty);
            return Task.CompletedTask;
        }

        private class TestExtensionBundleManager : IExtensionBundleManager
        {
            public Task<ExtensionBundleDetails> GetExtensionBundleDetails() => Task.FromResult<ExtensionBundleDetails>(null);

            public Task<string> GetExtensionBundlePath(HttpClient httpClient = null) => Task.FromResult<string>(null);

            public Task<string> GetExtensionBundlePath() => Task.FromResult<string>(null);

            public bool IsExtensionBundleConfigured() => false;

        }

        private class TestEntity : TableEntity
        {
            public string Name { get; set; }

            public string Region { get; set; }

            public int Status { get; set; }
        }
    }
}
