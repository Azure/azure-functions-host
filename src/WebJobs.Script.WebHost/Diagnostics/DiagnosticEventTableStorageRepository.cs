// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public partial class DiagnosticEventTableStorageRepository : IDiagnosticEventRepository, IDisposable
    {
        internal const string TableNamePrefix = "AzureFunctionsDiagnosticEvents";
        private const int LogFlushInterval = 1000 * 60 * 10; // 10 minutes
        private const int TableCreationMaxRetryCount = 5;

        private readonly Timer _flushLogsTimer;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IEnvironment _environment;
        private readonly IAzureTableStorageProvider _azureTableStorageProvider;
        private readonly ILogger<DiagnosticEventTableStorageRepository> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly object _syncLock = new object();

        private ConcurrentDictionary<string, DiagnosticEvent> _events = new ConcurrentDictionary<string, DiagnosticEvent>();
        private TableServiceClient _tableClient;
        private TableClient _diagnosticEventsTable;
        private string _hostId;
        private bool _disposed = false;
        private bool _purged = false;
        private string _tableName;
        private bool _isEnabled = true;

        internal DiagnosticEventTableStorageRepository(IHostIdProvider hostIdProvider, IEnvironment environment, IScriptHostManager scriptHostManager,
            IAzureTableStorageProvider azureTableStorageProvider, ILogger<DiagnosticEventTableStorageRepository> logger, int logFlushInterval)
        {
            _hostIdProvider = hostIdProvider;
            _environment = environment;
            _serviceProvider = scriptHostManager as IServiceProvider;
            _logger = logger;
            _flushLogsTimer = new Timer(OnFlushLogs, null, logFlushInterval, logFlushInterval);
            _azureTableStorageProvider = azureTableStorageProvider;
        }

        public DiagnosticEventTableStorageRepository(IHostIdProvider hostIdProvider, IEnvironment environment, IScriptHostManager scriptHost,
            IAzureTableStorageProvider azureTableStorageProvider, ILogger<DiagnosticEventTableStorageRepository> logger)
            : this(hostIdProvider, environment, scriptHost, azureTableStorageProvider, logger, LogFlushInterval) { }

        internal TableServiceClient TableClient
        {
            get
            {
                if (_tableClient is null && !_environment.IsPlaceholderModeEnabled())
                {
                    if (!_azureTableStorageProvider.TryCreateHostingTableServiceClient(out _tableClient))
                    {
                        DisableService();
                        Logger.ServiceDisabledFailedToCreateClient(_logger);
                        return _tableClient;
                    }

                    try
                    {
                        // When using RBAC, we need "Storage Table Data Contributor" as we require to list, create and delete tables and query/insert/delete entities.
                        // Testing permissions by listing tables, creating and deleting a test table.
                        var testTable = _tableClient.GetTableClient($"{TableNamePrefix}Check");
                        _ = TableStorageHelpers.TableExists(testTable, _tableClient);
                        _ = testTable.CreateIfNotExists();
                        _ = testTable.Delete();
                    }
                    catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.Conflict || rfe.ErrorCode == TableErrorCode.TableBeingDeleted)
                    {
                        // The table is being deleted or there could be a conflict for several instances initializing.
                        // We can ignore this error as it is not a failure and we tested the permissions.
                    }
                    catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.Forbidden)
                    {
                        DisableService();
                        Logger.ServiceDisabledUnauthorizedClient(_logger, rfe);
                    }
                    catch (Exception ex)
                    {
                        // We failed to connect to the table storage account. This could be due to a transient error or a configuration issue, such network issues.
                        // We will disable the service.
                        DisableService();
                        Logger.ServiceDisabledUnableToConnectToStorage(_logger, ex);
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

        private void DisableService()
        {
            _isEnabled = false;
            StopTimer();
            _events.Clear();
        }

        internal TableClient GetDiagnosticEventsTable(DateTime? now = null)
        {
            if (TableClient != null)
            {
                now = now ?? DateTime.UtcNow;
                string currentTableName = GetTableName(now.Value);

                // update the table reference when date rolls over to a new month
                if (_diagnosticEventsTable == null || currentTableName != _tableName)
                {
                    _tableName = currentTableName;
                    _diagnosticEventsTable = TableClient.GetTableClient(_tableName);
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
            Logger.PurgingDiagnosticEvents(_logger, DiagnosticEvent.CurrentEventVersion);

            bool tableDeleted = false;

            await Utility.InvokeWithRetriesAsync(async () =>
            {
                try
                {
                    var tables = (await TableStorageHelpers.ListTablesAsync(TableClient, TableNamePrefix)).ToList();

                    foreach (var table in tables)
                    {
                        var tableQuery = table.QueryAsync<DiagnosticEvent>(cancellationToken: default);

                        await foreach (var record in tableQuery)
                        {
                            // Delete table if it doesn't have records with EventVersion
                            if (string.IsNullOrEmpty(record.EventVersion) == true)
                            {
                                Logger.DeletingTableWithoutEventVersion(_logger, table.Name);
                                await table.DeleteAsync();
                                tableDeleted = true;
                                break;
                            }

                            // If the table does have EventVersion, query if it is an outdated version
                            if (string.Compare(DiagnosticEvent.CurrentEventVersion, record.EventVersion, StringComparison.Ordinal) > 0)
                            {
                                Logger.DeletingTableWithOutdatedEventVersion(_logger, table.Name);
                                await table.DeleteAsync();
                                tableDeleted = true;
                                break;
                            }
                        }
                    }

                    _purged = true;
                }
                catch (Exception ex)
                {
                    Logger.ErrorPurgingDiagnosticEventVersions(_logger, ex);
                }
            }, maxRetries: 5, retryInterval: TimeSpan.FromSeconds(5));

            if (tableDeleted)
            {
                // Wait for 30 seconds to allow the table to be deleted before proceeding to avoid a potential race.
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        }

        internal virtual async Task FlushLogs(TableClient table = null)
        {
            // TableClient is initialized lazily and it will stop the timer that schedules flush logs whenever it fails to initialize.
            // We need to check if the TableClient is null before proceeding. This helps when the first time the property is accessed is as part of the FlushLogs method.
            // We should not have any events stored pending to be written since WriteDiagnosticEvent will check for an initialized TableClient.
            if (_environment.IsPlaceholderModeEnabled() || TableClient is null || !IsEnabled())
            {
                return;
            }

            if (IsPrimaryHost() && !_purged)
            {
                await PurgePreviousEventVersions();
            }

            try
            {
                table = table ?? GetDiagnosticEventsTable();

                if (table == null)
                {
                    Logger.UnableToGetTableReference(_logger);
                    DisableService();
                    return;
                }

                bool tableCreated = await TableStorageHelpers.CreateIfNotExistsAsync(table, TableClient, TableCreationMaxRetryCount);
                if (tableCreated)
                {
                    Logger.QueueingBackgroundTablePurge(_logger);
                    TableStorageHelpers.QueueBackgroundTablePurge(table, TableClient, TableNamePrefix, _logger);
                }
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Forbidden)
            {
                // If we reach this point, we already checked for permissions on TableClient initialization. It is possible that the permissions changed after the initialization or any storage firewall/network configuration changed.
                // We will log the error and disable the service.
                Logger.UnableToGetTableReferenceOrCreateTable(_logger, ex);
                DisableService();
                Logger.ServiceDisabledUnauthorizedClient(_logger, ex);
            }
            catch (Exception ex)
            {
                Logger.UnableToGetTableReferenceOrCreateTable(_logger, ex);
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

        internal async Task ExecuteBatchAsync(ConcurrentDictionary<string, DiagnosticEvent> events, TableClient table)
        {
            try
            {
                var batch = new List<TableTransactionAction>();
                foreach (string errorCode in events.Keys)
                {
                    var diagnosticEvent = events[errorCode];
                    diagnosticEvent.Message = Sanitizer.Sanitize(diagnosticEvent.Message);
                    diagnosticEvent.Details = Sanitizer.Sanitize(diagnosticEvent.Details);
                    TableTransactionAction insertAction = new TableTransactionAction(TableTransactionActionType.Add, diagnosticEvent);
                    batch.Add(insertAction);
                }
                await table.SubmitTransactionAsync(batch);
                events.Clear();
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Forbidden)
            {
                // If we reach this point, we already checked for permissions on TableClient initialization.
                // It is possible that the permissions changed after the initialization, any firewall/network rules were changed or it's a custom role where we don't have permissions to write entities.
                // We will log the error and disable the service.
                Logger.UnableToWriteDiagnosticEvents(_logger, ex);
                DisableService();
                Logger.ServiceDisabledUnauthorizedClient(_logger, ex);
            }
            catch (Exception ex)
            {
                Logger.UnableToWriteDiagnosticEvents(_logger, ex);
            }
        }

        public void WriteDiagnosticEvent(DateTime timestamp, string errorCode, LogLevel level, string message, string helpLink, Exception exception)
        {
            if (TableClient is null || string.IsNullOrEmpty(HostId))
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

        public bool IsEnabled()
        {
            return _isEnabled;
        }

        private bool IsPrimaryHost()
        {
            var primaryHostStateProvider = _serviceProvider?.GetService<IPrimaryHostStateProvider>();
            if (primaryHostStateProvider is null)
            {
                Logger.PrimaryHostStateProviderNotAvailable(_logger);
                return false;
            }

            return primaryHostStateProvider.IsPrimary;
        }

        private void StopTimer()
        {
            Logger.StoppingFlushLogsTimer(_logger);
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