// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Scale
{
    public class TableStorageScaleMetricsRepositoryTests
    {
        private const string TestHostId = "testhostid";

        private readonly TableStorageScaleMetricsRepository _repository;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly Mock<IHostIdProvider> _hostIdProviderMock;
        private readonly ScaleOptions _scaleOptions;

        public TableStorageScaleMetricsRepositoryTests()
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            _hostIdProviderMock = new Mock<IHostIdProvider>(MockBehavior.Strict);
            _hostIdProviderMock.Setup(p => p.GetHostIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(TestHostId);
            _scaleOptions = new ScaleOptions
            {
                MetricsPurgeEnabled = false
            };
            _loggerProvider = new TestLoggerProvider();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            _repository = new TableStorageScaleMetricsRepository(configuration, _hostIdProviderMock.Object, new OptionsWrapper<ScaleOptions>(_scaleOptions), loggerFactory);

            EmptyMetricsTableAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public async Task InvalidStorageConnection_Handled()
        {
            var configuration = new ConfigurationBuilder().Build();
            Assert.Null(configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage));

            var options = new ScaleOptions();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            var localRepository = new TableStorageScaleMetricsRepository(configuration, _hostIdProviderMock.Object, new OptionsWrapper<ScaleOptions>(options), loggerFactory);

            var monitor1 = new TestScaleMonitor1();
            var monitor2 = new TestScaleMonitor2();
            var monitor3 = new TestScaleMonitor3();
            var monitors = new IScaleMonitor[] { monitor1, monitor2, monitor3 };
            var result = await localRepository.ReadMetricsAsync(monitors);
            Assert.Empty(result);

            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Single(logs);
            Assert.Equal("Azure Storage connection string is empty or invalid. Unable to read/write scale metrics.", logs[0].FormattedMessage);

            _loggerProvider.ClearAllLogMessages();
            Dictionary<IScaleMonitor, ScaleMetrics> metricsMap = new Dictionary<IScaleMonitor, ScaleMetrics>();
            metricsMap.Add(monitor1, new TestScaleMetrics1 { Count = 10 });
            metricsMap.Add(monitor2, new TestScaleMetrics2 { Num = 50 });
            metricsMap.Add(monitor3, new TestScaleMetrics3 { Length = 100 });
            await localRepository.WriteMetricsAsync(metricsMap);
        }

        [Fact]
        public async Task WriteMetricsAsync_PersistsMetrics()
        {
            var monitor1 = new TestScaleMonitor1();
            var monitor2 = new TestScaleMonitor2();
            var monitor3 = new TestScaleMonitor3();
            var monitors = new IScaleMonitor[] { monitor1, monitor2, monitor3 };

            var result = await _repository.ReadMetricsAsync(monitors);
            Assert.Equal(3, result.Count);

            // simulate 10 sample iterations
            for (int i = 0; i < 10; i++)
            {
                Dictionary<IScaleMonitor, ScaleMetrics> metricsMap = new Dictionary<IScaleMonitor, ScaleMetrics>();

                metricsMap.Add(monitor1, new TestScaleMetrics1 { Count = i });
                metricsMap.Add(monitor2, new TestScaleMetrics2 { Num = i });
                metricsMap.Add(monitor3, new TestScaleMetrics3 { Length = i });

                await _repository.WriteMetricsAsync(metricsMap);
            }

            // read the metrics back
            result = await _repository.ReadMetricsAsync(monitors);
            Assert.Equal(3, result.Count);

            var monitorMetricsList = result[monitor1];
            for (int i = 0; i < 10; i++)
            {
                var currSample = (TestScaleMetrics1)monitorMetricsList[i];
                Assert.Equal(i, currSample.Count);
                Assert.NotEqual(default(DateTime), currSample.Timestamp);
            }

            monitorMetricsList = result[monitor2];
            for (int i = 0; i < 10; i++)
            {
                var currSample = (TestScaleMetrics2)monitorMetricsList[i];
                Assert.Equal(i, currSample.Num);
                Assert.NotEqual(default(DateTime), currSample.Timestamp);
            }

            monitorMetricsList = result[monitor3];
            for (int i = 0; i < 10; i++)
            {
                var currSample = (TestScaleMetrics3)monitorMetricsList[i];
                Assert.Equal(i, currSample.Length);
                Assert.NotEqual(default(DateTime), currSample.Timestamp);
            }

            // if no monitors are presented result will be empty
            monitors = new IScaleMonitor[0];
            result = await _repository.ReadMetricsAsync(monitors);
            Assert.Equal(0, result.Count);
        }

        [Fact]
        public async Task ReadMetricsAsync_FiltersExpiredMetrics()
        {
            var monitor1 = new TestScaleMonitor1();
            var monitors = new IScaleMonitor[] { monitor1 };

            // add a bunch of expired samples
            var batch = new TableBatchOperation();
            for (int i = 5; i > 0; i--)
            {
                var metrics = new TestScaleMetrics1
                {
                    Count = i
                };
                var now = DateTime.UtcNow - _scaleOptions.ScaleMetricsMaxAge - TimeSpan.FromMinutes(i);
                await _repository.AccumulateMetricsBatchAsync(batch, monitor1, new ScaleMetrics[] { metrics }, now);
            }

            // add a few samples that aren't expired
            for (int i = 3; i > 0; i--)
            {
                var metrics = new TestScaleMetrics1
                {
                    Count = 77
                };
                var now = DateTime.UtcNow - TimeSpan.FromSeconds(i);
                await _repository.AccumulateMetricsBatchAsync(batch, monitor1, new ScaleMetrics[] { metrics }, now);
            }

            await _repository.ExecuteBatchSafeAsync(batch);
            
            var result = await _repository.ReadMetricsAsync(monitors);

            var resultMetrics = result[monitor1].Cast<TestScaleMetrics1>().ToArray();
            Assert.Equal(3, resultMetrics.Length);
            Assert.All(resultMetrics, p => Assert.Equal(77, p.Count));
        }

        [Fact]
        public async Task ReadMetricsAsync_NoMetricsForMonitor_ReturnsEmpty()
        {
            var monitor1 = new TestScaleMonitor1();
            var monitors = new IScaleMonitor[] { monitor1 };

            var result = await _repository.ReadMetricsAsync(monitors);
            Assert.Equal(1, result.Count);
            Assert.Empty(result[monitor1]);
        }

        [Fact]
        public async Task ReadMetricsAsync_InvalidMonitor_ReturnsEmpty()
        {
            var monitor1 = new TestInvalidScaleMonitor();
            var monitors = new IScaleMonitor[] { monitor1 };

            var result = await _repository.ReadMetricsAsync(monitors);
            Assert.Equal(1, result.Count);
            Assert.Empty(result[monitor1]);

            var log = _loggerProvider.GetAllLogMessages().Single();
            Assert.Equal("Monitor Microsoft.Azure.WebJobs.Script.Tests.TestInvalidScaleMonitor doesn't implement Microsoft.Azure.WebJobs.Host.Scale.IScaleMonitor`1[TMetrics].", log.FormattedMessage);
        }

        [Fact]
        public async Task TableRead_ManyRows_Succeeds()
        {
            var monitor1 = new TestScaleMonitor1();
            var monitors = new IScaleMonitor[] { monitor1 };

            var metricsTable = _repository.GetMetricsTable();
            await _repository.CreateIfNotExistsAsync(metricsTable);
            TableBatchOperation batch = new TableBatchOperation();

            int numRows = 500;
            for (int i = 0; i < numRows; i++)
            {
                var sample = new TestScaleMetrics1 { Count = i };
                await Task.Delay(5);
                batch.Add(TableStorageScaleMetricsRepository.CreateMetricsInsertOperation(sample, TestHostId, monitor1.Descriptor));

                if (batch.Count % 100 == 0)
                {
                    await metricsTable.ExecuteBatchAsync(batch);
                    batch = new TableBatchOperation();
                }
            }
            if (batch.Count > 0)
            {
                await metricsTable.ExecuteBatchAsync(batch);
            }

            var results = await _repository.ReadMetricsAsync(monitors);
            Assert.Equal(1, results.Count);
            Assert.Equal(numRows, results[monitor1].Count);

            // verify results are returned in the order they were inserted (ascending
            // time order) and the timestamps are monotonically increasing
            var metrics = results[monitor1].ToArray();
            for (int i = 0; i < numRows - 1; i++)
            {
                for (int j = i + 1; j < numRows; j++)
                {
                    var m1 = (TestScaleMetrics1)metrics[i];
                    var m2 = (TestScaleMetrics1)metrics[j];
                    Assert.True(m1.Count < m2.Count);
                    Assert.True(metrics[i].Timestamp < metrics[j].Timestamp);
                }
            }
        }

        [Fact]
        public async Task WriteAsync_RollsToNewTable()
        {
            // delete any existing non-current metrics tables
            var tables = await _repository.ListOldMetricsTablesAsync();
            foreach (var table in tables)
            {
                await table.DeleteIfExistsAsync();
            }

            // set now to months back
            int numTables = 3;
            var startDate = DateTime.UtcNow.AddMonths(-1 * numTables);

            // now start writing metrics in each old month
            DateTime now = startDate;
            var monitor1 = new TestScaleMonitor1();
            var monitors = new IScaleMonitor[] { monitor1 };
            for (int i = 0; i < numTables; i++)
            {
                var table = _repository.GetMetricsTable(now);

                var monitorMetrics = new List<ScaleMetrics>();
                for (int j = 0; j < 10; j++)
                {
                    monitorMetrics.Add(new TestScaleMetrics1 { Count = j });
                }

                await _repository.WriteMetricsAsync(monitor1, monitorMetrics, now);

                now = now.AddMonths(1);
            }

            // verify 3 tables were created
            tables = await _repository.ListOldMetricsTablesAsync();
            Assert.Equal(3, tables.Count());

            // make sure the current metrics table isn't in the list
            var currTable = _repository.GetMetricsTable();
            Assert.False(tables.Select(p => p.Name).Contains(currTable.Name));

            // delete the tables
            await _repository.DeleteOldMetricsTablesAsync();

            tables = await _repository.ListOldMetricsTablesAsync();
            Assert.Empty(tables);
        }

        [Fact]
        public async Task QueueBackgroundMetricsTablePurge_PurgesTables()
        {
            // delete any existing non-current metrics tables
            var tables = await _repository.ListOldMetricsTablesAsync();
            foreach (var table in tables)
            {
                await table.DeleteIfExistsAsync();
            }

            // create 3 old tables
            for (int i = 0; i < 3; i++)
            {
                var table = _repository.TableClient.GetTableReference($"{TableStorageScaleMetricsRepository.TableNamePrefix}Test{i}");
                await _repository.CreateIfNotExistsAsync(table);
            }

            // verify tables were created
            tables = await _repository.ListOldMetricsTablesAsync();
            Assert.Equal(3, tables.Count());

            // queue the background purge
            _repository.QueueBackgroundMetricsTablePurge(delaySeconds: 0);

            // wait for the purge to complete
            await TestHelpers.Await(async () =>
            {
                tables = await _repository.ListOldMetricsTablesAsync();
                return tables.Count() == 0;
            }, timeout: 5000);
        }

        [Fact]
        public async Task LogStorageException_LogsDetails()
        {
            StorageException ex = null;
            var table =_repository.TableClient.GetTableReference("dne");
            var continuationToken = new TableContinuationToken();
            try
            {
                await table.ExecuteQuerySegmentedAsync(new TableQuery(), continuationToken);
            }
            catch (StorageException e)
            {
                ex = e;
            }

            Assert.NotNull(ex);
            _repository.LogStorageException(ex);

            // ensure that inner storage exception details are dumped as part of log
            var logs = _loggerProvider.GetAllLogMessages();
            var errorLog = logs.Single();
            Assert.True(errorLog.FormattedMessage.Contains("An unhandled storage exception occurred when reading/writing scale metrics"));
            Assert.True(errorLog.FormattedMessage.Contains("StatusMessage:Not Found"));
            Assert.True(errorLog.FormattedMessage.Contains("ErrorMessage:The table specified does not exist."));

            _loggerProvider.ClearAllLogMessages();
            var batch = new TableBatchOperation();
            batch.Add(TableOperation.Insert(new DynamicTableEntity("testpk", "testrk")));
            try
            {
                await table.ExecuteBatchAsync(batch);
            }
            catch (StorageException e)
            {
                ex = e;
            }

            Assert.NotNull(ex);
            _repository.LogStorageException(ex);

            logs = _loggerProvider.GetAllLogMessages();
            errorLog = logs.Single();
            Assert.True(errorLog.FormattedMessage.Contains("An unhandled storage exception occurred when reading/writing scale metrics"));
            Assert.True(errorLog.FormattedMessage.Contains("Element 0 in the batch returned an unexpected response code"));
            Assert.True(errorLog.FormattedMessage.Contains("StatusMessage:0:The table specified does not exist."));
            Assert.True(errorLog.FormattedMessage.Contains("ErrorMessage:0:The table specified does not exist."));
        }

        private async Task EmptyMetricsTableAsync()
        {
            // We empty the table rather than delete it, because deletes don't happen
            // instantly (they're queued for processing by Azure Storage). That causes
            // conflict issues when the tests attempt to recreate the table.
            var metricsTable = _repository.GetMetricsTable();
            await EmptyTableAsync(metricsTable);
        }

        private async Task EmptyTableAsync(CloudTable table)
        {
            var results = await _repository.ExecuteQuerySafeAsync(table, new TableQuery());
            if (results.Any())
            {
                TableBatchOperation batch = new TableBatchOperation();
                foreach (var entity in results)
                {
                    batch.Add(TableOperation.Delete(entity));

                    if (batch.Count == 100)
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
    }
}
