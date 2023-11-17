// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Moq.Protected;
using Xunit;
using System.Linq;
using Microsoft.Azure.Cosmos.Table;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Diagnostics
{
    public class DiagnosticEventTableStorageRepositoryTests
    {
        private const string TestHostId = "testhostid";
        private readonly IHostIdProvider _hostIdProvider;
        private readonly TestLoggerProvider _loggerProvider;
        private IConfiguration _configuration;
        private ILogger<DiagnosticEventTableStorageRepository> _logger;

        public DiagnosticEventTableStorageRepositoryTests()
        {
            _configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            _hostIdProvider = new FixedHostIdProvider(TestHostId);

            _loggerProvider = new TestLoggerProvider();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            _logger = loggerFactory.CreateLogger<DiagnosticEventTableStorageRepository>();
        }

        [Fact]
        public async Task TimerFlush_CalledOnExpectedInterval()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            int flushInterval = 10;
            Mock<DiagnosticEventTableStorageRepository> mockDiagnosticEventTableStorageRepository = new Mock<DiagnosticEventTableStorageRepository>
                (_configuration, _hostIdProvider, testEnvironment, NullLogger<DiagnosticEventTableStorageRepository>.Instance, flushInterval);

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
        public void WriteDiagnostic_LogsError_whenHostIdNotSet()
        {
            IEnvironment testEnvironment = new TestEnvironment();

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_configuration, null, testEnvironment, _logger);

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
                new DiagnosticEventTableStorageRepository(_configuration, _hostIdProvider, testEnvironment, _logger);

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
                new DiagnosticEventTableStorageRepository(_configuration, _hostIdProvider, testEnvironment, _logger);
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
            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(configuration, _hostIdProvider, testEnvironment, _logger);
            DateTime dateTime = new DateTime(2021, 1, 1);
            var cloudTable = repository.GetDiagnosticEventsTable(dateTime);
            Assert.Null(cloudTable);
            var messages = _loggerProvider.GetAllLogMessages();
            Assert.Equal(messages[0].FormattedMessage, "Azure Storage connection string is empty or invalid. Unable to write diagnostic events.");
        }

        [Fact]
        public async Task QueueBackgroundDiagnosticsEventsTablePurge_PurgesTables()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_configuration, _hostIdProvider, testEnvironment, _logger);

            // delete any existing non-current diagnostics events tables
            string tablePrefix = DiagnosticEventTableStorageRepository.TableNamePrefix;
            var currentTable = repository.GetDiagnosticEventsTable();
            var tables = await TableStorageHelpers.ListOldTablesAsync(currentTable, repository.TableClient, tablePrefix);
            foreach (var table in tables)
            {
                await table.DeleteIfExistsAsync();
            }

            // create 3 old tables
            for (int i = 0; i < 3; i++)
            {
                var table = repository.TableClient.GetTableReference($"{tablePrefix}Test{i}");
                await TableStorageHelpers.CreateIfNotExistsAsync(table, 2);
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
        public async Task FlushLogs_LogsErrorAndClearsEvents_WhenTableCreatingFails()
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

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(configuration, _hostIdProvider, testEnvironment, _logger);

            // Act
            repository.WriteDiagnosticEvent(DateTime.UtcNow, "eh1", LogLevel.Information, "This is the message", "https://fwlink/", new Exception("exception message"));
            await repository.FlushLogs();

            // Assert 
            var logMessage = _loggerProvider.GetAllLogMessages().SingleOrDefault(m => m.FormattedMessage.Contains("Unable to get table reference"));
            Assert.NotNull(logMessage);

            var messagePresent = _loggerProvider.GetAllLogMessages().Any(m => m.FormattedMessage.Contains("Azure Storage connection string is empty or invalid. Unable to write diagnostic events."));
            Assert.True(messagePresent);
            
            Assert.Equal(0, repository.Events.Values.Count());
        }

        [Fact]
        public async Task ExecuteBatchAsync_WritesToTableStorage()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_configuration, _hostIdProvider, testEnvironment, _logger);

            var table = repository.GetDiagnosticEventsTable();
            await TableStorageHelpers.CreateIfNotExistsAsync(table, 2);
            await EmptyTableAsync(table);

            var dateTime = DateTime.UtcNow;
            var diagnosticEvent = new DiagnosticEvent("hostId", dateTime);
            var events = new ConcurrentDictionary<string, DiagnosticEvent>();
            events.TryAdd("EC123", diagnosticEvent);
            await repository.ExecuteBatchAsync(events, table);

            var results = ExecuteQuery(table, new TableQuery());
            Assert.Equal(results.Count(), 1);
        }

        [Fact]
        public async Task FlushLogs_WritesToTableStorage()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_configuration, _hostIdProvider, testEnvironment, _logger);

            var table = repository.GetDiagnosticEventsTable();
            await TableStorageHelpers.CreateIfNotExistsAsync(table, 2);
            await EmptyTableAsync(table);

            var dateTime = DateTime.UtcNow;
            var diagnosticEvent = new DiagnosticEvent("hostId", dateTime);
            repository.Events.TryAdd("EC123", diagnosticEvent);
            await repository.FlushLogs(table);

            var results = ExecuteQuery(table, new TableQuery());
            Assert.Equal(results.Count(), 1);
        }

        [Fact]
        public async Task ExecuteBatchAsync_LogsError()
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            DiagnosticEventTableStorageRepository repository =
                new DiagnosticEventTableStorageRepository(_configuration, _hostIdProvider, testEnvironment, _logger);

            var tableClient = repository.TableClient;
            var table = tableClient.GetTableReference("aa");

            var dateTime = DateTime.UtcNow;
            var diagnosticEvent = new DiagnosticEvent("hostId", dateTime);

            var events = new ConcurrentDictionary<string, DiagnosticEvent>();
            events.TryAdd("EC123", diagnosticEvent);
            await repository.ExecuteBatchAsync(events, table);

            ExecuteQuery(table, new TableQuery());
            string message = _loggerProvider.GetAllLogMessages()[0].FormattedMessage;
            Assert.True(message.StartsWith("Unable to write diagnostic events to table storage"));
        }

        private async Task EmptyTableAsync(CloudTable table)
        {
            var results = ExecuteQuery(table, new TableQuery());
            if (results.Any())
            {
                TableBatchOperation batch = new TableBatchOperation();
                foreach (var entity in results)
                {
                    batch.Add(TableOperation.Delete(entity));

                    if (batch.Count == 1)
                    {
                        var result = await table.ExecuteBatchAsync(batch);
                        batch = new TableBatchOperation();
                    }
                }

                if (batch.Count > 0)
                {
                    await table.ExecuteBatchAsync(batch);
                }
            }
        }

        internal IEnumerable<DynamicTableEntity> ExecuteQuery(CloudTable table, TableQuery query)
        {
            if (!table.Exists())
            {
                return Enumerable.Empty<DynamicTableEntity>();
            }

            return table.ExecuteQuery(query);
        }

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
