// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Azure.WebJobs.Script.WebHost.Scale;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class TableStorageScaleMetricsRepository : IScaleMetricsRepository
    {
        // from https://docs.microsoft.com/en-us/dotnet/api/microsoft.azure.cosmos.table.tablebatchoperation
        internal const int MaxTableOperationBatchCount = 100;

        internal const string TableNamePrefix = "AzureFunctionsScaleMetrics";
        internal const string MonitorIdPropertyName = "MonitorId";
        private const string SampleTimestampPropertyName = "SampleTimestamp";
        private const int MetricsPurgeDelaySeconds = 30;
        private const int DefaultTableCreationRetries = 3;

        private readonly IHostIdProvider _hostIdProvider;
        private readonly IAzureTableStorageProvider _azureTableStorageProvider;
        private readonly ScaleOptions _scaleOptions;
        private readonly ILogger _logger;
        private readonly int _tableCreationRetries;
        private TableServiceClient _tableServiceClient;

        public TableStorageScaleMetricsRepository(IHostIdProvider hostIdProvider, IOptions<ScaleOptions> scaleOptions, ILoggerFactory loggerFactory, IAzureTableStorageProvider azureTableStorageProvider)
            : this(hostIdProvider, scaleOptions, loggerFactory, azureTableStorageProvider, DefaultTableCreationRetries)
        {
        }

        internal TableStorageScaleMetricsRepository(IHostIdProvider hostIdProvider, IOptions<ScaleOptions> scaleOptions, ILoggerFactory loggerFactory,
            IAzureTableStorageProvider azureTableStorageProvider, int tableCreationRetries)
        {
            _hostIdProvider = hostIdProvider;
            _azureTableStorageProvider = azureTableStorageProvider;
            _scaleOptions = scaleOptions.Value;
            _logger = loggerFactory.CreateLogger<TableStorageScaleMetricsRepository>();
            _tableCreationRetries = tableCreationRetries;
        }

        internal TableServiceClient TableServiceClient
        {
            get
            {
                if (_tableServiceClient is null && !_azureTableStorageProvider.TryCreateHostingTableServiceClient(out _tableServiceClient))
                {
                    _logger.LogError("Azure Storage connection string is empty or invalid. Unable to read/write scale metrics.");
                }

                return _tableServiceClient;
            }
        }

        internal TableClient GetMetricsTable(DateTime? now = null)
        {
            TableClient table = null;

            if (TableServiceClient != null)
            {
                // we'll roll automatically to a new table once per month
                now = now ?? DateTime.UtcNow;
                string tableName = string.Format("{0}{1:yyyyMM}", TableNamePrefix, now.Value);
                return TableServiceClient.GetTableClient(tableName);
            }

            return table;
        }

        public async Task<IDictionary<IScaleMonitor, IList<ScaleMetrics>>> ReadMetricsAsync(IEnumerable<IScaleMonitor> monitors)
        {
            try
            {
                var monitorMetrics = new Dictionary<IScaleMonitor, IList<ScaleMetrics>>();
                var metricsTable = GetMetricsTable();
                if (metricsTable == null || !monitors.Any())
                {
                    return monitorMetrics;
                }

                var recentMetrics = await ReadRecentMetrics(metricsTable);

                var entitiesByMonitorId = recentMetrics.ToLookup(p => p.GetString(MonitorIdPropertyName), StringComparer.OrdinalIgnoreCase);
                foreach (var monitor in monitors)
                {
                    var currMonitorMetrics = new List<ScaleMetrics>();
                    monitorMetrics[monitor] = currMonitorMetrics;

                    Type metricsType = GetMonitorScaleMetricsTypeOrNull(monitor);
                    if (metricsType != null)
                    {
                        var monitorEntities = entitiesByMonitorId[monitor.Descriptor.Id];
                        foreach (var entity in monitorEntities)
                        {
                            var convertedMetrics = (ScaleMetrics)TableEntityConverter.ToObject(metricsType, entity);
                            var timestamp = entity.GetDateTime(SampleTimestampPropertyName);
                            if (timestamp.HasValue)
                            {
                                convertedMetrics.Timestamp = timestamp.Value;
                            }
                            currMonitorMetrics.Add(convertedMetrics);
                        }

                        // entities are stored in the table recent to oldest, so we reverse
                        // to give consumers a list in ascending time order
                        currMonitorMetrics.Reverse();
                    }
                }

                return monitorMetrics;
            }
            catch (RequestFailedException e)
            {
                LogStorageException(e);
                throw;
            }
        }

        public async Task WriteMetricsAsync(IDictionary<IScaleMonitor, ScaleMetrics> monitorMetrics)
        {
            if (!monitorMetrics.Any())
            {
                return;
            }

            try
            {
                // Page through all the metrics and persist them in batches, ensuring we stay below
                // the max operation count for each batch.
                int skip = 0;
                var currMetricsBatch = monitorMetrics.Take(MaxTableOperationBatchCount).ToArray();
                while (currMetricsBatch.Length > 0)
                {
                    var batch = new List<TableTransactionAction>();
                    foreach (var pair in currMetricsBatch)
                    {
                        await AccumulateMetricsBatchAsync(batch, pair.Key, new ScaleMetrics[] { pair.Value });
                    }

                    await ExecuteBatchSafeAsync(batch);

                    skip += currMetricsBatch.Length;
                    currMetricsBatch = monitorMetrics.Skip(skip).Take(MaxTableOperationBatchCount).ToArray();
                }
            }
            catch (RequestFailedException e)
            {
                LogStorageException(e);
                throw;
            }
        }

        internal async Task WriteMetricsAsync(IScaleMonitor monitor, IEnumerable<ScaleMetrics> metrics, DateTime? now = null)
        {
            var batch = new List<TableTransactionAction>();
            await AccumulateMetricsBatchAsync(batch, monitor, metrics, now);
            await ExecuteBatchSafeAsync(batch, now);
        }

        internal Type GetMonitorScaleMetricsTypeOrNull(IScaleMonitor monitor)
        {
            var monitorInterfaceType = monitor.GetType().GetInterfaces().SingleOrDefault(p => p.IsGenericType && p.GetGenericTypeDefinition() == typeof(IScaleMonitor<>));
            if (monitorInterfaceType != null)
            {
                return monitorInterfaceType.GetGenericArguments()[0];
            }
            // we require the monitor to implement the generic interface in order to know
            // what type to deserialize into
            _logger.LogWarning($"Monitor {monitor.GetType().FullName} doesn't implement {typeof(IScaleMonitor<>)}.");
            return null;
        }

        internal void LogStorageException(RequestFailedException ex)
        {
            _logger.LogError(ex, "An unhandled storage exception occurred when reading/writing scale metrics: {reason}", ex.Message);
        }

        internal async Task ExecuteBatchSafeAsync(List<TableTransactionAction> batch, DateTime? now = null)
        {
            var metricsTable = GetMetricsTable(now);
            if (metricsTable != null && batch.Any())
            {
                try
                {
                    // TODO: handle paging and errors
                    await metricsTable.SubmitTransactionAsync(batch);
                }
                catch (RequestFailedException e)
                {
                    if (IsTableNotFound(e))
                    {
                        // create the table and retry
                        await CreateIfNotExistsAsync(metricsTable);
                        await metricsTable.SubmitTransactionAsync(batch);
                        return;
                    }

                    throw;
                }
            }
        }

        internal async Task CreateIfNotExistsAsync(TableClient table, int retryDelayMS = 1000)
        {
            bool tableCreated = await TableStorageHelpers.CreateIfNotExistsAsync(table, TableServiceClient, _tableCreationRetries, retryDelayMS);

            if (tableCreated && _scaleOptions.MetricsPurgeEnabled)
            {
                // when we roll over to a new table, it's a good time to
                // do a background purge of any old tables
                QueueBackgroundMetricsTablePurge();
            }
        }

        internal async Task AccumulateMetricsBatchAsync(List<TableTransactionAction> batch, IScaleMonitor monitor, IEnumerable<ScaleMetrics> metrics, DateTime? now = null)
        {
            if (!metrics.Any())
            {
                return;
            }

            string hostId = await _hostIdProvider.GetHostIdAsync(CancellationToken.None);

            foreach (var sample in metrics)
            {
                var operation = CreateMetricsInsertOperation(sample, hostId, monitor.Descriptor, now);
                batch.Add(operation);
            }
        }

        internal static TableTransactionAction CreateMetricsInsertOperation(ScaleMetrics metrics, string hostId, ScaleMonitorDescriptor descriptor, DateTime? now = null)
        {
            now = now ?? DateTime.UtcNow;

            // Use an inverted ticks rowkey to order the table in descending order, allowing us to easily
            // query for latest logs. Adding a guid as part of the key to ensure uniqueness.
            string rowKey = TableStorageHelpers.GetRowKey(now.Value);

            var entity = TableEntityConverter.ToEntity(metrics, hostId, rowKey, metrics.Timestamp);
            entity.Add(MonitorIdPropertyName, descriptor.Id);

            // We map the sample timestamp to its own column so it doesn't conflict with the built in column.
            // We want to ensure that timestamp values for returned metrics are precise and monotonically
            // increasing when ordered results are returned. The built in timestamp doesn't guarantee this.
            entity.Add(SampleTimestampPropertyName, metrics.Timestamp);

            return new TableTransactionAction(TableTransactionActionType.Add, entity);
        }

        internal async Task<IEnumerable<TableEntity>> ExecuteQuerySafeAsync(TableClient metricsTable, string query)
        {
            try
            {
                List<TableEntity> results = new List<TableEntity>();

                await foreach (var result in metricsTable.QueryAsync<TableEntity>(query))
                {
                    results.Add(result);
                }

                return results;
            }
            catch (RequestFailedException e)
            {
                if (IsTableNotFound(e))
                {
                    return Enumerable.Empty<TableEntity>();
                }

                throw;
            }
        }

        internal async Task<IEnumerable<TableEntity>> ReadRecentMetrics(TableClient metricsTable)
        {
            // generate a query that will return the most recent metrics
            // based on the configurable max age
            string hostId = await _hostIdProvider.GetHostIdAsync(CancellationToken.None);
            var cuttoff = DateTime.UtcNow - _scaleOptions.ScaleMetricsMaxAge;
            var ticks = string.Format("{0:D19}", DateTime.MaxValue.Ticks - cuttoff.Ticks);
            return await ExecuteQuerySafeAsync(metricsTable, TableClient.CreateQueryFilter($"PartitionKey eq {hostId} and RowKey lt {ticks}"));
        }

        internal async Task<IEnumerable<TableClient>> ListOldMetricsTablesAsync()
        {
            var currTable = GetMetricsTable();
            var tables = await TableStorageHelpers.ListTablesAsync(TableServiceClient, TableNamePrefix);
            return tables.Where(p => !string.Equals(currTable.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Fire and forget metrics table cleanup.
        /// </summary>
        internal void QueueBackgroundMetricsTablePurge(int delaySeconds = MetricsPurgeDelaySeconds)
        {
            var tIgnore = Task.Run(async () =>
            {
                try
                {
                    // the deletion is queued with a delay to allow for clock skew across
                    // instances, thus giving time for any in flight operations against the
                    // previous table to complete.
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    await DeleteOldMetricsTablesAsync();
                }
                catch (Exception e)
                {
                    // best effort - if purge fails we log and ignore
                    // we'll try again another time
                    _logger.LogError(e, "Error occurred when attempting to delete old metrics tables.");
                }
            });
        }

        internal async Task DeleteOldMetricsTablesAsync()
        {
            var tablesToDelete = await TableStorageHelpers.ListOldTablesAsync(GetMetricsTable(), TableServiceClient, TableNamePrefix);
            _logger.LogDebug($"Deleting {tablesToDelete.Count()} old metrics tables.");
            foreach (var table in tablesToDelete)
            {
                _logger.LogDebug($"Deleting metrics table '{table.Name}'");
                await table.DeleteAsync();
                _logger.LogDebug($"Metrics table '{table.Name}' deleted.");
            }
        }

        private static bool IsTableNotFound(RequestFailedException exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            if (exception.Status != (int)HttpStatusCode.NotFound)
            {
                return false;
            }

            return exception.ErrorCode == "TableNotFound";
        }
    }
}
