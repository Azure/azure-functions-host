// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Azure.WebJobs.Script.WebHost.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Storage;
using Microsoft.Extensions.Configuration;
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

        private readonly IConfiguration _configuration;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly ScaleOptions _scaleOptions;
        private readonly ILogger _logger;
        private readonly int _tableCreationRetries;
        private readonly IDelegatingHandlerProvider _delegatingHandlerProvider;
        private CloudTableClient _tableClient;

        public TableStorageScaleMetricsRepository(IConfiguration configuration, IHostIdProvider hostIdProvider, IOptions<ScaleOptions> scaleOptions, ILoggerFactory loggerFactory, IEnvironment environment)
            : this(configuration, hostIdProvider, scaleOptions, loggerFactory, DefaultTableCreationRetries, new DefaultDelegatingHandlerProvider(environment))
        {
        }

        internal TableStorageScaleMetricsRepository(IConfiguration configuration, IHostIdProvider hostIdProvider, IOptions<ScaleOptions> scaleOptions, ILoggerFactory loggerFactory,
            int tableCreationRetries, IDelegatingHandlerProvider delegatingHandlerProvider)
        {
            _configuration = configuration;
            _hostIdProvider = hostIdProvider;
            _scaleOptions = scaleOptions.Value;
            _logger = loggerFactory.CreateLogger<TableStorageScaleMetricsRepository>();
            _tableCreationRetries = tableCreationRetries;
            _delegatingHandlerProvider = delegatingHandlerProvider ?? throw new ArgumentNullException(nameof(delegatingHandlerProvider));
        }

        internal CloudTableClient TableClient
        {
            get
            {
                if (_tableClient == null)
                {
                    string storageConnectionString = _configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
                    if (!string.IsNullOrEmpty(storageConnectionString) &&
                        CloudStorageAccount.TryParse(storageConnectionString, out CloudStorageAccount account))
                    {
                        var restConfig = new RestExecutorConfiguration { DelegatingHandler = _delegatingHandlerProvider.Create() };
                        var tableClientConfig = new TableClientConfiguration { RestExecutorConfiguration = restConfig };

                        _tableClient = new CloudTableClient(account.TableStorageUri, account.Credentials, tableClientConfig);
                    }
                    else
                    {
                        _logger.LogError("Azure Storage connection string is empty or invalid. Unable to read/write scale metrics.");
                    }
                }
                return _tableClient;
            }
        }

        internal CloudTable GetMetricsTable(DateTime? now = null)
        {
            CloudTable table = null;

            if (TableClient != null)
            {
                // we'll roll automatically to a new table once per month
                now = now ?? DateTime.UtcNow;
                string tableName = string.Format("{0}{1:yyyyMM}", TableNamePrefix, now.Value);
                return TableClient.GetTableReference(tableName);
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

                var entitiesByMonitorId = recentMetrics.ToLookup(p => p.Properties[MonitorIdPropertyName].StringValue, StringComparer.OrdinalIgnoreCase);
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
                            if (entity.Properties.TryGetValue(SampleTimestampPropertyName, out EntityProperty value) && value.DateTime.HasValue)
                            {
                                convertedMetrics.Timestamp = value.DateTime.Value;
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
            catch (StorageException e)
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
                    var batch = new TableBatchOperation();
                    foreach (var pair in currMetricsBatch)
                    {
                        await AccumulateMetricsBatchAsync(batch, pair.Key, new ScaleMetrics[] { pair.Value });
                    }

                    await ExecuteBatchSafeAsync(batch);

                    skip += currMetricsBatch.Length;
                    currMetricsBatch = monitorMetrics.Skip(skip).Take(MaxTableOperationBatchCount).ToArray();
                }
            }
            catch (StorageException e)
            {
                LogStorageException(e);
                throw;
            }
        }

        internal async Task WriteMetricsAsync(IScaleMonitor monitor, IEnumerable<ScaleMetrics> metrics, DateTime? now = null)
        {
            var batch = new TableBatchOperation();
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

        internal void LogStorageException(StorageException ex)
        {
            _logger.LogError(ex, $"An unhandled storage exception occurred when reading/writing scale metrics: {ex.ToString()}");
        }

        internal async Task ExecuteBatchSafeAsync(TableBatchOperation batch, DateTime? now = null)
        {
            var metricsTable = GetMetricsTable(now);
            if (metricsTable != null && batch.Any())
            {
                try
                {
                    // TODO: handle paging and errors
                    await metricsTable.ExecuteBatchAsync(batch);
                }
                catch (StorageException e)
                {
                    if (IsNotFoundTableNotFound(e))
                    {
                        // create the table and retry
                        await CreateIfNotExistsAsync(metricsTable);
                        await metricsTable.ExecuteBatchAsync(batch);
                        return;
                    }

                    throw;
                }
            }
        }

        internal async Task CreateIfNotExistsAsync(CloudTable table, int retryDelayMS = 1000)
        {
            int attempt = 0;
            do
            {
                try
                {
                    bool tableCreated = true;
                    if (!table.Exists())
                    {
                        tableCreated = await table.CreateIfNotExistsAsync();
                    }

                    if (tableCreated && _scaleOptions.MetricsPurgeEnabled)
                    {
                        // when we roll over to a new table, it's a good time to
                        // do a background purge of any old tables
                        QueueBackgroundMetricsTablePurge();
                    }
                }
                catch (StorageException e)
                {
                    // Can get conflicts with multiple instances attempting to create
                    // the same table.
                    // Also, if a table queued up for deletion, we can get a conflict on create,
                    // though these should only happen in tests not production, because we only ever
                    // delete OLD tables and we'll never be attempting to recreate a table we just
                    // deleted outside of tests.
                    if (e.RequestInformation.HttpStatusCode == (int)HttpStatusCode.Conflict &&
                        attempt < _tableCreationRetries)
                    {
                        // wait a bit and try again
                        await Task.Delay(retryDelayMS);
                        continue;
                    }
                    throw;
                }

                return;
            }
            while (attempt++ < _tableCreationRetries);
        }

        internal async Task AccumulateMetricsBatchAsync(TableBatchOperation batch, IScaleMonitor monitor, IEnumerable<ScaleMetrics> metrics, DateTime? now = null)
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

        internal static TableOperation CreateMetricsInsertOperation(ScaleMetrics metrics, string hostId, ScaleMonitorDescriptor descriptor, DateTime? now = null)
        {
            now = now ?? DateTime.UtcNow;

            // Use an inverted ticks rowkey to order the table in descending order, allowing us to easily
            // query for latest logs. Adding a guid as part of the key to ensure uniqueness.
            string rowKey = TableStorageHelpers.GetRowKey(now.Value);

            var entity = TableEntityConverter.ToEntity(metrics, hostId, rowKey, metrics.Timestamp);
            entity.Properties.Add(MonitorIdPropertyName, EntityProperty.GeneratePropertyForString(descriptor.Id));

            // We map the sample timestamp to its own column so it doesn't conflict with the built in column.
            // We want to ensure that timestamp values for returned metrics are precise and monotonically
            // increasing when ordered results are returned. The built in timestamp doesn't guarantee this.
            entity.Properties.Add(SampleTimestampPropertyName, EntityProperty.GeneratePropertyForDateTimeOffset(metrics.Timestamp));

            return TableOperation.Insert(entity);
        }

        internal async Task<IEnumerable<DynamicTableEntity>> ExecuteQuerySafeAsync(CloudTable metricsTable, TableQuery query)
        {
            try
            {
                return await ExecuteQueryWithContinuationAsync(metricsTable, query);
            }
            catch (StorageException e)
            {
                if (IsNotFoundTableNotFound(e))
                {
                    return Enumerable.Empty<DynamicTableEntity>();
                }

                throw;
            }
        }

        internal async Task<IEnumerable<DynamicTableEntity>> ReadRecentMetrics(CloudTable metricsTable)
        {
            // generate a query that will return the most recent metrics
            // based on the configurable max age
            string hostId = await _hostIdProvider.GetHostIdAsync(CancellationToken.None);
            var cuttoff = DateTime.UtcNow - _scaleOptions.ScaleMetricsMaxAge;
            var ticks = string.Format("{0:D19}", DateTime.MaxValue.Ticks - cuttoff.Ticks);
            string filter = TableQuery.CombineFilters(
                TableQuery.GenerateFilterCondition(nameof(TableEntity.PartitionKey), QueryComparisons.Equal, hostId),
                TableOperators.And,
                TableQuery.GenerateFilterCondition(nameof(TableEntity.RowKey), QueryComparisons.LessThan, ticks));
            var query = new TableQuery().Where(filter);

            return await ExecuteQuerySafeAsync(metricsTable, query);
        }

        private async Task<List<DynamicTableEntity>> ExecuteQueryWithContinuationAsync(CloudTable metricsTable, TableQuery query)
        {
            List<DynamicTableEntity> results = new List<DynamicTableEntity>();
            TableContinuationToken continuationToken = null;

            do
            {
                var result = await metricsTable.ExecuteQuerySegmentedAsync(query, continuationToken);
                continuationToken = result.ContinuationToken;
                results.AddRange(result.Results);
            }
            while (continuationToken != null);

            return results;
        }

        private async Task<IEnumerable<CloudTable>> ListMetricsTablesAsync()
        {
            List<CloudTable> tables = new List<CloudTable>();
            TableContinuationToken continuationToken = null;

            do
            {
                var results = await TableClient.ListTablesSegmentedAsync(TableNamePrefix, continuationToken);
                continuationToken = results.ContinuationToken;
                tables.AddRange(results.Results);
            }
            while (continuationToken != null);

            return tables;
        }

        internal async Task<IEnumerable<CloudTable>> ListOldMetricsTablesAsync()
        {
            var currTable = GetMetricsTable();
            var tables = await ListMetricsTablesAsync();
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
                    // best effort - if purge fails we log an ignore
                    // we'll try again another time
                    _logger.LogError(e, "Error occurred when attempting to delete old metrics tables.");
                }
            });
        }

        internal async Task DeleteOldMetricsTablesAsync()
        {
            var tablesToDelete = await ListOldMetricsTablesAsync();
            _logger.LogDebug($"Deleting {tablesToDelete.Count()} old metrics tables.");
            foreach (var table in tablesToDelete)
            {
                _logger.LogDebug($"Deleting metrics table '{table.Name}'");
                await table.DeleteIfExistsAsync();
                _logger.LogDebug($"Metrics table '{table.Name}' deleted.");
            }
        }

        private static bool IsNotFoundTableNotFound(StorageException exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            var result = exception.RequestInformation;
            if (result == null)
            {
                return false;
            }

            if (result.HttpStatusCode != (int)HttpStatusCode.NotFound)
            {
                return false;
            }

            var extendedInformation = result.ExtendedErrorInformation;
            if (extendedInformation == null)
            {
                return false;
            }

            return extendedInformation.ErrorCode == "TableNotFound";
        }
    }
}
