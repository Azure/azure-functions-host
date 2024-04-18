// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEventTableStorageRepository : IDiagnosticEventRepository, IDisposable
    {
        internal const string TableNamePrefix = "AzureFunctionsDiagnosticEvents";
        private const int LogFlushInterval = 1000 * 60 * 10; // 10 minutes
        private const int TableCreationMaxRetryCount = 5;

        private readonly Timer _flushLogsTimer;
        private readonly IConfiguration _configuration;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IEnvironment _environment;
        private readonly ILogger<DiagnosticEventTableStorageRepository> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly object _syncLock = new object();

        private ConcurrentDictionary<string, DiagnosticEvent> _events = new ConcurrentDictionary<string, DiagnosticEvent>();
        private CloudTableClient _tableClient;
        private CloudTable _diagnosticEventsTable;
        private IPrimaryHostStateProvider _primaryHostStateProvider;
        private string _hostId;
        private bool _disposed = false;
        private bool _purged = false;
        private string _tableName;

        internal DiagnosticEventTableStorageRepository(IConfiguration configuration, IHostIdProvider hostIdProvider, IEnvironment environment, IScriptHostManager scriptHostManager,
            ILogger<DiagnosticEventTableStorageRepository> logger, int logFlushInterval)
        {
            _configuration = configuration;
            _hostIdProvider = hostIdProvider;
            _environment = environment;
            _serviceProvider = scriptHostManager as IServiceProvider;
            _logger = logger;
            _flushLogsTimer = new Timer(OnFlushLogs, null, logFlushInterval, logFlushInterval);
        }

        public DiagnosticEventTableStorageRepository(IConfiguration configuration, IHostIdProvider hostIdProvider, IEnvironment environment, IScriptHostManager scriptHost,
            ILogger<DiagnosticEventTableStorageRepository> logger)
            : this(configuration, hostIdProvider, environment, scriptHost, logger, LogFlushInterval) { }

        internal CloudTableClient TableClient
        {
            get
            {
                if (!_environment.IsPlaceholderModeEnabled() && _tableClient == null)
                {
                    string storageConnectionString = _configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
                    if (!string.IsNullOrEmpty(storageConnectionString)
                        && CloudStorageAccount.TryParse(storageConnectionString, out CloudStorageAccount account))
                    {
                        var tableClientConfig = new TableClientConfiguration();
                        _tableClient = new CloudTableClient(account.TableStorageUri, account.Credentials, tableClientConfig);
                    }
                    else
                    {
                        _logger.LogError("Azure Storage connection string is empty or invalid. Unable to write diagnostic events.");
                    }
                }

                return _tableClient;
            }
        }

        internal string HostId
        {
            get
            {
                if (!_environment.IsPlaceholderModeEnabled() && string.IsNullOrEmpty(_hostId))
                {
                    _hostId = _hostIdProvider?.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                return _hostId;
            }
        }

        internal ConcurrentDictionary<string, DiagnosticEvent> Events => _events;

        internal IPrimaryHostStateProvider HostStateProvider => _primaryHostStateProvider ??= _serviceProvider?.GetService<IPrimaryHostStateProvider>();

        internal CloudTable GetDiagnosticEventsTable(DateTime? now = null)
        {
            if (TableClient != null)
            {
                now = now ?? DateTime.UtcNow;
                string currentTableName = GetTableName(now.Value);

                // update the table reference when date rolls over to a new month
                if (_diagnosticEventsTable == null || currentTableName != _tableName)
                {
                    _tableName = currentTableName;
                    _diagnosticEventsTable = TableClient.GetTableReference(_tableName);
                }
            }

            return _diagnosticEventsTable;
        }

        private static string GetTableName(DateTime date)
        {
            return $"{TableNamePrefix}{date:yyyyMM}";
        }

        protected internal virtual async void OnFlushLogs(object state)
        {
            await FlushLogs();
        }

        private async Task PurgePreviousEventVersions()
        {
            _logger.LogDebug("Purging diagnostic events with versions older than '{currentEventVersion}'.", DiagnosticEvent.CurrentEventVersion);

            await Utility.InvokeWithRetriesAsync(async () =>
            {
                try
                {
                    var tables = (await TableStorageHelpers.ListTablesAsync(TableClient, TableNamePrefix)).ToList();

                    foreach (var table in tables)
                    {
                        var tableRecords = await table.ExecuteQuerySegmentedAsync(new TableQuery<DiagnosticEvent>(), null);

                        // Skip tables that have 0 records
                        if (tableRecords.Results.Count == 0)
                        {
                            continue;
                        }

                        // Delete table if it doesn't have records with EventVersion
                        var eventVersionDoesNotExists = tableRecords.Results.Any(record => string.IsNullOrEmpty(record.EventVersion) == true);
                        if (eventVersionDoesNotExists)
                        {
                            _logger.LogDebug("Deleting table '{tableName}' as it contains records without an EventVersion.", table.Name);
                            await table.DeleteIfExistsAsync();
                            continue;
                        }

                        // If the table does have EventVersion, query if it is an outdated version
                        var eventVersionOutdated = tableRecords.Results.Any(record => string.Compare(DiagnosticEvent.CurrentEventVersion, record.EventVersion, StringComparison.Ordinal) > 0);
                        if (eventVersionOutdated)
                        {
                            _logger.LogDebug("Deleting table '{tableName}' as it contains records with an outdated EventVersion.", table.Name);
                            await table.DeleteIfExistsAsync();
                        }
                    }

                    _purged = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred when attempting to purge previous diagnostic event versions.");
                }
            }, maxRetries: 5, retryInterval: TimeSpan.FromSeconds(5));
        }

        internal virtual async Task FlushLogs(CloudTable table = null)
        {
            if (_environment.IsPlaceholderModeEnabled())
            {
                return;
            }

            if (HostStateProvider is not null && HostStateProvider.IsPrimary && !_purged)
            {
                _ = PurgePreviousEventVersions();
            }

            try
            {
                table = table ?? GetDiagnosticEventsTable();

                if (table == null)
                {
                    _logger.LogError("Unable to get table reference. Aborting write operation.");
                    StopTimer();
                    return;
                }

                bool tableCreated = await TableStorageHelpers.CreateIfNotExistsAsync(table, TableCreationMaxRetryCount);
                if (tableCreated)
                {
                    _logger.LogDebug("Queueing background table purge.");
                    TableStorageHelpers.QueueBackgroundTablePurge(table, TableClient, TableNamePrefix, _logger);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to get table reference or create table. Aborting write operation.");
                // Clearing the memory cache to avoid memory build up.
                _events.Clear();
                return;
            }

            // Assigning a new empty directory to reset the event count in the new duration window.
            // All existing events are logged to other logging pipelines already.
            ConcurrentDictionary<string, DiagnosticEvent> tempDictionary = _events;
            _events = new ConcurrentDictionary<string, DiagnosticEvent>();
            if (!tempDictionary.IsEmpty)
            {
                await ExecuteBatchAsync(tempDictionary, table);
            }
        }

        internal async Task ExecuteBatchAsync(ConcurrentDictionary<string, DiagnosticEvent> events, CloudTable table)
        {
            try
            {
                var batch = new TableBatchOperation();
                foreach (string errorCode in events.Keys)
                {
                    var diagnosticEvent = events[errorCode];
                    diagnosticEvent.Message = Sanitizer.Sanitize(diagnosticEvent.Message);
                    diagnosticEvent.Details = Sanitizer.Sanitize(diagnosticEvent.Details);
                    TableOperation insertOperation = TableOperation.Insert(diagnosticEvent);
                    batch.Add(insertOperation);
                }
                await table.ExecuteBatchAsync(batch);
                events.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to write diagnostic events to table storage.");
            }
        }

        public void WriteDiagnosticEvent(DateTime timestamp, string errorCode, LogLevel level, string message, string helpLink, Exception exception)
        {
            if (TableClient == null || string.IsNullOrEmpty(HostId))
            {
                return;
            }

            var diagnosticEvent = new DiagnosticEvent(HostId, timestamp)
            {
                ErrorCode = errorCode,
                HelpLink = helpLink,
                Message = message,
                LogLevel = level,
                Details = exception?.ToFormattedString(),
                HitCount = 1
            };

            if (!_events.TryAdd(errorCode, diagnosticEvent))
            {
                lock (_syncLock)
                {
                    _events[errorCode].Timestamp = timestamp;
                    _events[errorCode].HitCount++;
                }
            }
        }

        private void StopTimer()
        {
            _logger.LogInformation("Stopping the flush logs timer.");
            _flushLogsTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_flushLogsTimer != null)
                    {
                        _flushLogsTimer.Dispose();
                    }

                    FlushLogs().GetAwaiter().GetResult();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}