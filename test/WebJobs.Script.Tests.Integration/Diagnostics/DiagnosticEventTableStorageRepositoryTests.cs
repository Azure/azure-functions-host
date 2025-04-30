// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Azure.Data.Tables;
using Moq;
using Moq.Protected;
using Xunit;
using Microsoft.Azure.WebJobs.Script.Tests.Integration.Fixtures;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Diagnostics
{
    public class DiagnosticEventTableStorageRepositoryTests : IClassFixture<AzuriteFixture>
    {
        private const string TestHostId = "testhostid";
        private readonly IHostIdProvider _hostIdProvider;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly IAzureTableStorageProvider _azureTableStorageProvider;
        private readonly AzuriteFixture _azurite;

        private ILogger<DiagnosticEventTableStorageRepository> _logger;
        private Mock<IScriptHostManager> _scriptHostMock;

        public DiagnosticEventTableStorageRepositoryTests(AzuriteFixture azurite)
        {
            _azurite = azurite;
            _hostIdProvider = new FixedHostIdProvider(TestHostId);

            var mockPrimaryHostStateProvider = new Mock<IPrimaryHostStateProvider>(MockBehavior.Strict);
            mockPrimaryHostStateProvider.Setup(p => p.IsPrimary).Returns(false);

            _scriptHostMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            _scriptHostMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IPrimaryHostStateProvider))).Returns(mockPrimaryHostStateProvider.Object);

            _loggerProvider = new TestLoggerProvider();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            _logger = loggerFactory.CreateLogger<DiagnosticEventTableStorageRepository>();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "AzureWebJobsStorage", _azurite.GetConnectionString() },
                })
                .Build();
            _azureTableStorageProvider = TestHelpers.GetAzureTableStorageProvider(configuration);
        }

        [Fact]
        public async Task TimerFlush_CalledOnExpectedInterval()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            int flushInterval = 10;
            Mock<DiagnosticEventTableStorageRepository> mockDiagnosticEventTableStorageRepository = new Mock<DiagnosticEventTableStorageRepository>
                (_hostIdProvider, testEnvironment, _scriptHostMock.Object, _azureTableStorageProvider, NullLogger<DiagnosticEventTableStorageRepository>.Instance, flushInterval);

            DiagnosticEventTableStorageRepository repository = mockDiagnosticEventTableStorageRepository.Object;

            int numFlushes = 0;
            mockDiagnosticEventTableStorageRepository.Protected().Setup("OnFlushLogs", ItExpr.IsAny<object>())
                .Callback<object>((state) =>
                {
                    numFlushes++;
                });

            await TestHelpers.Await(() => numFlushes >= 5, timeout: 2000, pollingInterval: 100, userMessageCallback: () => $"Expected numFlushes >= 5; Actual: {numFlushes}");

            mockDiagnosticEventTableStorageRepository.VerifyAll();
        }

        [Fact]
        public void WriteDiagnostic_LogsError_WhenHostIdNotSet()
        {
            IEnvironment testEnvironment = new TestEnvironment();

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(null, testEnvironment, _scriptHostMock.Object, _azureTableStorageProvider, _logger);

            repository.WriteDiagnosticEvent(DateTime.UtcNow, "eh1", LogLevel.Information, "This is the message", "https://fwlink/", new Exception("exception message"));

            var messages = _loggerProvider.GetAllLogMessages();
            Assert.Equal(0, repository.Events.Values.Count());
        }

        [Fact]
        public async Task WriteDiagnostic_HasTheCorrectHitCount_WhenCalledFromMultipleThreadsConcurrently()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_hostIdProvider, testEnvironment, _scriptHostMock.Object, _azureTableStorageProvider, _logger);

            var eventTask1 = Task.Run(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    Thread.Sleep(10);
                    repository.WriteDiagnosticEvent(DateTime.UtcNow, "fn0001", LogLevel.Information, "This is the message", "https://fwlink/", new Exception("exception message"));
                    repository.WriteDiagnosticEvent(DateTime.UtcNow, "fn0002", LogLevel.Information, "This is the message", "https://fwlink/", new Exception("exception message"));
                }
            });

            var eventTask2 = Task.Run(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    Thread.Sleep(10);
                    repository.WriteDiagnosticEvent(DateTime.UtcNow, "fn0001", LogLevel.Information, "This is the message", "https://fwlink/", new Exception("exception message"));
                    repository.WriteDiagnosticEvent(DateTime.UtcNow, "fn0003", LogLevel.Information, "This is the message", "https://fwlink/", new Exception("exception message"));
                }
            });

            var eventTask3 = Task.Run(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    Thread.Sleep(10);
                    repository.WriteDiagnosticEvent(DateTime.UtcNow, "fn0001", LogLevel.Information, "This is the message", "https://fwlink/", new Exception("exception message"));
                    repository.WriteDiagnosticEvent(DateTime.UtcNow, "fn0002", LogLevel.Information, "This is the message", "https://fwlink/", new Exception("exception message"));
                }
            });

            await Task.WhenAll(eventTask1, eventTask2, eventTask3);
            Assert.Equal(600, repository.Events["fn0001"].HitCount);
            Assert.Equal(400, repository.Events["fn0002"].HitCount);
            Assert.Equal(200, repository.Events["fn0003"].HitCount);
        }

        [Fact]
        public void GetDiagnosticEventsTable_ReturnsExpectedValue_WhenSpecialized()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_hostIdProvider, testEnvironment, _scriptHostMock.Object, _azureTableStorageProvider, _logger);
            DateTime dateTime = new DateTime(2021, 1, 1);
            var cloudTable = repository.GetDiagnosticEventsTable(dateTime);
            Assert.NotNull(cloudTable);
            Assert.NotNull(repository.TableClient);
            Assert.Equal(cloudTable.Name, $"{DiagnosticEventTableStorageRepository.TableNamePrefix}202101");
        }

        [Fact]
        public void GetDiagnosticEventsTable_LogsError_StorageConnectionStringIsNotPresent()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            var configuration = new ConfigurationBuilder().Build();
            var localStorageProvider = TestHelpers.GetAzureTableStorageProvider(configuration);
            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_hostIdProvider, testEnvironment, _scriptHostMock.Object, localStorageProvider, _logger);

            // The repository should be initially enabled and then disabled after TableServiceClient failure.
            Assert.True(repository.IsEnabled());
            DateTime dateTime = new DateTime(2021, 1, 1);
            var cloudTable = repository.GetDiagnosticEventsTable(dateTime);
            Assert.Null(cloudTable);
            var messages = _loggerProvider.GetAllLogMessages();
            var errorIntializingPresent = messages.Any(m => m.FormattedMessage.Contains("We couldn’t initialize the Table Storage Client using the 'AzureWebJobsStorage' connection string. We are unable to record diagnostic events, so the diagnostic logging service is being stopped. Please check the 'AzureWebJobsStorage' connection string in Application Settings."));
            Assert.True(errorIntializingPresent);
            Assert.False(repository.IsEnabled());
        }

        [Fact]
        public async Task QueueBackgroundDiagnosticsEventsTablePurge_PurgesTables()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_hostIdProvider, testEnvironment, _scriptHostMock.Object, _azureTableStorageProvider, _logger);

            // delete any existing non-current diagnostics events tables
            string tablePrefix = DiagnosticEventTableStorageRepository.TableNamePrefix;
            var currentTable = repository.GetDiagnosticEventsTable();
            var tables = await TableStorageHelpers.ListOldTablesAsync(currentTable, repository.TableClient, tablePrefix);
            foreach (var table in tables)
            {
                await table.DeleteAsync();
            }

            // create 3 old tables
            for (int i = 0; i < 3; i++)
            {
                var tableId = Guid.NewGuid().ToString("N").Substring(0, 5);
                var table = repository.TableClient.GetTableClient($"{tablePrefix}Test{tableId}");
                await TableStorageHelpers.CreateIfNotExistsAsync(table, repository.TableClient, 2);
            }

            // verify tables were created
            tables = await TableStorageHelpers.ListOldTablesAsync(currentTable, repository.TableClient, tablePrefix);
            Assert.Equal(3, tables.Count());

            // queue the background purge
            TableStorageHelpers.QueueBackgroundTablePurge(currentTable, repository.TableClient, tablePrefix, NullLogger.Instance, 0);

            // wait for the purge to complete
            await TestHelpers.Await(async () =>
            {
                tables = await TableStorageHelpers.ListOldTablesAsync(currentTable, repository.TableClient, tablePrefix);
                return tables.Count() == 0;
            }, timeout: 5000);
        }

        [Fact]
        public async Task QueueBackgroundDiagnosticsEventsTablePurge_PurgesOnlyDiagnosticTables()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_hostIdProvider, testEnvironment, _scriptHostMock.Object, _azureTableStorageProvider, _logger);

            // delete any existing non-current diagnostics events tables
            string tablePrefix = DiagnosticEventTableStorageRepository.TableNamePrefix;
            var currentTable = repository.GetDiagnosticEventsTable();
            var tables = await TableStorageHelpers.ListOldTablesAsync(currentTable, repository.TableClient, tablePrefix);
            foreach (var table in tables)
            {
                await table.DeleteAsync();
            }

            // create 1 old table
            var tableId = Guid.NewGuid().ToString("N").Substring(0, 5);
            var oldTable = repository.TableClient.GetTableClient($"{tablePrefix}Test{tableId}");
            await TableStorageHelpers.CreateIfNotExistsAsync(oldTable, repository.TableClient, 2);

            // create a non-diagnostic table
            var nonDiagnosticTable = repository.TableClient.GetTableClient("NonDiagnosticTable");
            await TableStorageHelpers.CreateIfNotExistsAsync(nonDiagnosticTable, repository.TableClient, 2);

            // verify tables were created
            Assert.True(await TableStorageHelpers.TableExistAsync(oldTable, repository.TableClient));
            Assert.True(await TableStorageHelpers.TableExistAsync(nonDiagnosticTable, repository.TableClient));

            // queue the background purge
            TableStorageHelpers.QueueBackgroundTablePurge(currentTable, repository.TableClient, tablePrefix, NullLogger.Instance, 0);

            // wait for the purge to complete
            await TestHelpers.Await(async () =>
            {
                // verify that only the diagnostic table was deleted
                var diagnosticTableExist = await TableStorageHelpers.TableExistAsync(oldTable, repository.TableClient);
                var nonDiagnosticTableExists = await TableStorageHelpers.TableExistAsync(nonDiagnosticTable, repository.TableClient);

                return !diagnosticTableExist && nonDiagnosticTableExists;
            }, timeout: 5000);
        }

        [Fact]
        public async Task FlushLogs_OnPrimaryHost_DoesNotTryToPurgeEvents_WhenTableClientNotInitialized()
        {
            // Arrange
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "AzureWebJobsStorage", null }
            };

            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddInMemoryCollection(testData)
                .Build();

            var mockPrimaryHostStateProvider = new Mock<IPrimaryHostStateProvider>(MockBehavior.Strict);
            mockPrimaryHostStateProvider.Setup(p => p.IsPrimary).Returns(true);

            var scriptHostMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            scriptHostMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IPrimaryHostStateProvider))).Returns(mockPrimaryHostStateProvider.Object);

            var localStorageProvider = TestHelpers.GetAzureTableStorageProvider(configuration);
            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_hostIdProvider, testEnvironment, scriptHostMock.Object, localStorageProvider, _logger);

            // Act
            await repository.FlushLogs();

            // Assert
            var createFailureMessagePresent = _loggerProvider.GetAllLogMessages().Any(m => m.FormattedMessage.Contains("We couldn’t initialize the Table Storage Client using the 'AzureWebJobsStorage' connection string. We are unable to record diagnostic events, so the diagnostic logging service is being stopped. Please check the 'AzureWebJobsStorage' connection string in Application Settings."));
            Assert.True(createFailureMessagePresent);

            var purgeEventMessagePresent = _loggerProvider.GetAllLogMessages().Any(m => m.FormattedMessage.Contains("Purging diagnostic events with versions older than"));
            Assert.False(purgeEventMessagePresent);

        }

        [Theory]
        [InlineData("", 0, true)] // event version not present
        [InlineData("2021-01-01", 0, true)] // outdated version
        [InlineData("2024-05-01", 1, true)] // current version
        [InlineData("anything", 1, false)] // empty table gets skipped
        public async Task FlushLogs_OnPrimaryHost_PurgesPreviousEventVersionTables(string testEventVersion, int expectedTableCount, bool fillTable)
        {
            // Arrange
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            var mockPrimaryHostStateProvider = new Mock<IPrimaryHostStateProvider>(MockBehavior.Strict);
            mockPrimaryHostStateProvider.Setup(p => p.IsPrimary).Returns(true);

            var scriptHostMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            scriptHostMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IPrimaryHostStateProvider))).Returns(mockPrimaryHostStateProvider.Object);

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_hostIdProvider, testEnvironment, scriptHostMock.Object, _azureTableStorageProvider, _logger);

            // delete existing tables
            string tablePrefix = DiagnosticEventTableStorageRepository.TableNamePrefix;
            var currentTable = repository.GetDiagnosticEventsTable();
            var tables = await TableStorageHelpers.ListOldTablesAsync(currentTable, repository.TableClient, tablePrefix);
            foreach (var table in tables)
            {
                await table.DeleteAsync();
            }

            var tableId = Guid.NewGuid().ToString("N").Substring(0, 5);
            var testTable = repository.TableClient.GetTableClient($"{tablePrefix}Test{tableId}");
            await TableStorageHelpers.CreateIfNotExistsAsync(testTable, repository.TableClient, 2);

            // verify table were created
            tables = await TableStorageHelpers.ListOldTablesAsync(currentTable, repository.TableClient, tablePrefix);
            Assert.Equal(1, tables.Count());

            if (fillTable)
            {
                // add test diagnostic event
                var diagnosticEvent = CreateDiagnosticEvent(DateTime.UtcNow, "eh1", LogLevel.Information, "This is the message", "https://fwlink/", new Exception("exception message"), testEventVersion);
                await testTable.AddEntityAsync(diagnosticEvent);
            }

            // Act
            await repository.FlushLogs();

            // Assert
            await TestHelpers.Await(async () =>
            {
                var logMessage = _loggerProvider.GetAllLogMessages().SingleOrDefault(m => m.FormattedMessage.Contains("Purging diagnostic events with versions older than"));
                Assert.NotNull(logMessage);

                tables = await TableStorageHelpers.ListOldTablesAsync(currentTable, repository.TableClient, tablePrefix);
                Assert.Equal(expectedTableCount, tables.Count());

                return true;

            }, timeout: 5000);
        }

        [Fact]
        public async Task ExecuteBatchAsync_WritesToTableStorage()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_hostIdProvider, testEnvironment, _scriptHostMock.Object, _azureTableStorageProvider, _logger);

            var table = repository.GetDiagnosticEventsTable();
            await TableStorageHelpers.CreateIfNotExistsAsync(table, repository.TableClient, 2);
            await EmptyTableAsync(table);

            var dateTime = DateTime.UtcNow;
            var diagnosticEvent = new DiagnosticEvent("hostId", dateTime);
            var events = new ConcurrentDictionary<string, DiagnosticEvent>();
            events.TryAdd("EC123", diagnosticEvent);
            await repository.ExecuteBatchAsync(events, table);

            var results = ExecuteQuery(repository.TableClient, table);
            Assert.Equal(results.Count(), 1);
        }

        [Fact]
        public async Task FlushLogs_WritesToTableStorage()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_hostIdProvider, testEnvironment, _scriptHostMock.Object, _azureTableStorageProvider, _logger);

            var table = repository.GetDiagnosticEventsTable();
            await TableStorageHelpers.CreateIfNotExistsAsync(table, repository.TableClient, 2);
            await EmptyTableAsync(table);

            var dateTime = DateTime.UtcNow;
            var diagnosticEvent = new DiagnosticEvent("hostId", dateTime);
            repository.Events.TryAdd("EC123", diagnosticEvent);
            await repository.FlushLogs(table);

            var results = ExecuteQuery(repository.TableClient, table);
            Assert.Equal(results.Count(), 1);
        }

        [Fact]
        public async Task ExecuteBatchAsync_LogsError()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_hostIdProvider, testEnvironment, _scriptHostMock.Object, _azureTableStorageProvider, _logger);

            var tableClient = repository.TableClient;
            var table = tableClient.GetTableClient("aa");

            var dateTime = DateTime.UtcNow;
            var diagnosticEvent = new DiagnosticEvent("hostId", dateTime);

            var events = new ConcurrentDictionary<string, DiagnosticEvent>();
            events.TryAdd("EC123", diagnosticEvent);
            await repository.ExecuteBatchAsync(events, table);

            string message = _loggerProvider.GetAllLogMessages()[0].FormattedMessage;
            Assert.True(message.StartsWith("Unable to write diagnostic events to table storage"));
        }

        [Fact]
        public async Task FlushLogs_DisablesService_NoPermissions()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            // Tamper the connection string to simulate a permissions issue, we need a valid base64 connection string to create the TableServiceClient.
            var connectionString = _azurite.GetConnectionString();
            var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "AzureWebJobsStorage", connectionString.Replace(AzuriteFixture.AccountKey, TamperKey(AzuriteFixture.AccountKey)) },
            })
            .Build();
            var localStorageProvider = TestHelpers.GetAzureTableStorageProvider(configuration);

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_hostIdProvider, testEnvironment, _scriptHostMock.Object, localStorageProvider, _logger);

            // The repository should be initially enabled and then disabled after failing to initialize the TableServiceClient while flushing Logs.
            Assert.True(repository.IsEnabled());
            await repository.FlushLogs();
            Assert.False(repository.IsEnabled());
        }

        private DiagnosticEvent CreateDiagnosticEvent(DateTime timestamp, string errorCode, LogLevel level, string message, string helpLink, Exception exception, string eventVersion)
        {
            var diagnosticEvent = new DiagnosticEvent(TestHostId, timestamp)
            {
                ErrorCode = errorCode,
                HelpLink = helpLink,
                Message = message,
                LogLevel = level,
                Details = exception?.ToFormattedString(),
                HitCount = 1
            };

            diagnosticEvent.EventVersion = eventVersion ?? DiagnosticEvent.CurrentEventVersion;
            return diagnosticEvent;
        }

        private async Task EmptyTableAsync(TableClient table)
        {
            var results = table.QueryAsync<TableEntity>();

            await foreach (var entity in results)
            {
                await table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
            }
        }

        internal IEnumerable<TableEntity> ExecuteQuery(TableServiceClient tableClient, TableClient table)
        {
            if (!tableClient.Query(p => p.Name == table.Name).Any())
            {
                return Enumerable.Empty<TableEntity>();
            }

            return table.Query<TableEntity>();
        }

        private string TamperKey(string key) =>
            string.Create(key.Length, key, (destination, source) =>
            {
                source.AsSpan().CopyTo(destination);
                // We simply replace the first character to maintain a well-formed base64 key
                destination[0] = destination[0] == 'A' ? 'B' : 'A';
            });

        private class FixedHostIdProvider : IHostIdProvider
        {
            private readonly string _hostId;

            public FixedHostIdProvider(string hostId)
            {
                _hostId = hostId;
            }

            public Task<string> GetHostIdAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(_hostId);
            }
        }
    }
}