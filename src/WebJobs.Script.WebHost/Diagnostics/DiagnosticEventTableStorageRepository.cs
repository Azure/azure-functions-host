// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEventTableStorageRepository : IDiagnosticEventRepository, IDisposable
    {
        internal const string TableNamePrefix = "AzureFunctionsDiagnosticEvents";
        private const int LogFlushInterval = 1000 * 60 * 10; // 10 mins
        private readonly Timer _flushLogsTimer;

        private int _tableCreationRetries = 5;
        private ConcurrentDictionary<string, DiagnosticEvent> _events = new ConcurrentDictionary<string, DiagnosticEvent>();

        private IConfiguration _configuration;
        private IHostIdProvider _hostIdProvider;
        private IEnvironment _environment;
        private ILogger<DiagnosticEventTableStorageRepository> _logger;

        private CloudTableClient _tableClient;
        private CloudTable _diagnosticEventsTable;
        private string _hostId;
        private object _syncLock = new object();
        private bool _disposed = false;
        private string _tableName;

        internal DiagnosticEventTableStorageRepository(IConfiguration configuration, IHostIdProvider hostIdProvider, IEnvironment environment, ILogger<DiagnosticEventTableStorageRepository> logger, int logFlushInterval)
        {
            _configuration = configuration;
            _hostIdProvider = hostIdProvider;
            _environment = environment;
            _logger = logger;
            _flushLogsTimer = new Timer(OnFlushLogs, null, logFlushInterval, logFlushInterval);
        }

        public DiagnosticEventTableStorageRepository(IConfiguration configuration, IHostIdProvider hostIdProvider, IEnvironment environment, ILogger<DiagnosticEventTableStorageRepository> logger)
            : this(configuration, hostIdProvider, environment, logger, LogFlushInterval) { }

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

        internal ConcurrentDictionary<string, DiagnosticEvent> Events
        {
            get
            {
                return _events;
            }
        }

        internal CloudTable GetDiagnosticEventsTable(DateTime? now = null)
        {
            if (TableClient != null)
            {
                now = now ?? DateTime.UtcNow;
                string currentTableName = GetCurrentTableName(now.Value);

                // update the table reference when date rolls over to a new month
                if (_diagnosticEventsTable == null || currentTableName != _tableName)
                {
                    _tableName = currentTableName;
                    _diagnosticEventsTable = TableClient.GetTableReference(_tableName);
                }
            }

            return _diagnosticEventsTable;
        }

        private static string GetCurrentTableName(DateTime now)
        {
            return $"{TableNamePrefix}{now:yyyyMM}";
        }

        protected internal virtual async void OnFlushLogs(object state)
        {
            await FlushLogs();
        }

        internal virtual async Task FlushLogs(CloudTable table = null)
        {
            if (_environment.IsPlaceholderModeEnabled())
            {
                return;
            }

            try
            {
                table = table ?? GetDiagnosticEventsTable();

                if (table == null)
                {
                    _logger.LogError("Unable to get table reference. Aborting write operation");
                    StopTimer();
                    return;
                }

                bool tableCreated = await TableStorageHelpers.CreateIfNotExistsAsync(table, _tableCreationRetries);
                if (tableCreated)
                {
                    TableStorageHelpers.QueueBackgroundTablePurge(table, TableClient, TableNamePrefix, _logger);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unable to get table reference or create table. Aborting write operation.");
                // Clearing the memory cache to avoid memory build up.
                _events.Clear();
                return;
            }

            // Assigning a new empty directory to reset the event count in the new duration window.
            // All existing events are logged to other logging pipelines already.
            ConcurrentDictionary<string, DiagnosticEvent> tempDictionary = _events;
            _events = new ConcurrentDictionary<string, DiagnosticEvent>();
            if (tempDictionary.Count > 0)
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
                    TableOperation insertOperation = TableOperation.Insert(events[errorCode]);
                    batch.Add(insertOperation);
                }
                await table.ExecuteBatchAsync(batch);
                events.Clear();
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Unable to write diagnostic events to table storage:{e}");
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

        internal void StopTimer()
        {
            _logger.LogInformation("Stopping the flush logs timer");
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