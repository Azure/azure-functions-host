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

        private readonly Lazy<Timer> _flushLogsTimer;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IEnvironment _environment;
        private readonly IAzureTableStorageProvider _azureTableStorageProvider;
        private readonly ILogger<DiagnosticEventTableStorageRepository> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);

        private ConcurrentDictionary<string, DiagnosticEvent> _events = new ConcurrentDictionary<string, DiagnosticEvent>();
        private TableServiceClient _tableClient;
        private volatile bool _tableClientInitialized;
        private TableClient _diagnosticEventsTable;
        private string _hostId;
        private bool _disposed = false;
        private bool _purged = false;
        private string _tableName;
        private volatile bool _isEnabled = true;

        internal DiagnosticEventTableStorageRepository(IHostIdProvider hostIdProvider, IEnvironment environment, IScriptHostManager scriptHostManager,
            IAzureTableStorageProvider azureTableStorageProvider, ILogger<DiagnosticEventTableStorageRepository> logger, int logFlushInterval)
        {
            _hostIdProvider = hostIdProvider;
            _environment = environment;
            _serviceProvider = scriptHostManager as IServiceProvider;
            _logger = logger;
            _flushLogsTimer = new Lazy<Timer>(() => new Timer(OnFlushLogs, null, logFlushInterval, logFlushInterval));
            _azureTableStorageProvider = azureTableStorageProvider;
        }

        public DiagnosticEventTableStorageRepository(IHostIdProvider hostIdProvider, IEnvironment environment, IScriptHostManager scriptHost,
            IAzureTableStorageProvider azureTableStorageProvider, ILogger<DiagnosticEventTableStorageRepository> logger)
            : this(hostIdProvider, environment, scriptHost, azureTableStorageProvider, logger, LogFlushInterval) { }

        internal TableServiceClient TableClient => _tableClient;

        internal string HostId
        {
            get
            {
                if (string.IsNullOrEmpty(_hostId) && !_environment.IsPlaceholderModeEnabled())
                {
                    _hostId = _hostIdProvider?.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                return _hostId;
            }
        }

        internal ConcurrentDictionary<string, DiagnosticEvent> Events => _events;

        internal async Task InitializeTableClientAsync()
        {
            if (_tableClientInitialized || _disposed)
            {
                return;
            }

            try
            {
                await _initSemaphore.WaitAsync();
            }
            catch (ObjectDisposedException)
            {
                // Semaphore was disposed (race with disposal), exit early
                return;
            }

            try
            {
                if (_tableClientInitialized || _disposed)
                {
                    return;
                }

                if (!_azureTableStorageProvider.TryCreateHostingTableServiceClient(out _tableClient))
                {
                    DisableService();
                    Logger.ServiceDisabledFailedToCreateClient(_logger);
                    return;
                }

                try
                {
                    // When using RBAC, we need "Storage Table Data Contributor" as we require to list, create and delete tables and query/insert/delete entities.
                    // Testing permissions by listing tables, creating and deleting a test table.
                    var testTable = _tableClient.GetTableClient($"{TableNamePrefix}Check");
                    _ = await TableStorageHelpers.TableExistAsync(testTable, _tableClient);
                    _ = await testTable.CreateIfNotExistsAsync();
                    await testTable.DeleteAsync();
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
            finally
            {
                // Once initialization has been attempted (whether successful or not), we mark the table client as initialized
                // to avoid repeated initialization attempts. On failure paths, the service is disabled via DisableService().
                // Don't set if disposed to prevent race with disposal.
                if (!_disposed)
                {
                    _tableClientInitialized = true;
                }
                _initSemaphore.Release();
            }
        }

        private void DisableService()
        {
            _isEnabled = false;
            StopTimer();
            _events.Clear();
        }

        private async Task<bool> EnsureInitializedAsync()
        {
            if (_environment.IsPlaceholderModeEnabled() || !IsEnabled())
            {
                return false;
            }

            if (!_disposed)
            {
                await InitializeTableClientAsync();
            }

            if (_tableClient is null || !_tableClientInitialized || !IsEnabled())
            {
                return false;
            }

            return true;
        }

        internal async Task<TableClient> GetDiagnosticEventsTableAsync(DateTime? now = null)
        {
            if (!await EnsureInitializedAsync())
            {
                return null;
            }

            return GetDiagnosticEventsTable(now);
        }

        private TableClient GetDiagnosticEventsTable(DateTime? now = null)
        {
            now = now ?? DateTime.UtcNow;
            string currentTableName = GetTableName(now.Value);

            // update the table reference when date rolls over to a new month
            if (_diagnosticEventsTable == null || currentTableName != _tableName)
            {
                _tableName = currentTableName;
                _diagnosticEventsTable = _tableClient.GetTableClient(_tableName);
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
                    var tables = (await TableStorageHelpers.ListTablesAsync(_tableClient, TableNamePrefix)).ToList();

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
                catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Forbidden)
                {
                    // If we reach this point, we already checked for permissions on TableClient initialization.
                    // It is possible that the permissions changed after the initialization, any firewall/network rules were changed or it's a custom role where we don't have permissions to query entities.
                    // We will log the error and disable the service.
                    Logger.ErrorPurgingDiagnosticEventVersions(_logger, ex);
                    DisableService();
                    Logger.ServiceDisabledUnauthorizedClient(_logger, ex);
                }
                catch (Exception ex)
                {
                    // We failed to connect to the table storage account. This could be due to a transient error or a configuration issue (e.g., network problems).
                    // To avoid repeatedly retrying in a potentially unhealthy state, we will disable the service.
                    // The operation may succeed in a future instance if the underlying issue is resolved.
                    Logger.ErrorPurgingDiagnosticEventVersions(_logger, ex);
                    DisableService();
                    Logger.ServiceDisabledUnableToConnectToStorage(_logger, ex);
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
            if (!await EnsureInitializedAsync())
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

                bool tableCreated = await TableStorageHelpers.CreateIfNotExistsAsync(table, _tableClient, TableCreationMaxRetryCount);
                if (tableCreated)
                {
                    Logger.QueueingBackgroundTablePurge(_logger);
                    TableStorageHelpers.QueueBackgroundTablePurge(table, _tableClient, TableNamePrefix, _logger);
                }
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Forbidden)
            {
                // If we reach this point, we already checked for permissions on TableClient initialization. It is possible that the permissions changed after the initialization or any storage firewall/network configuration changed.
                // We will log the error and disable the service.
                Logger.UnableToGetTableReferenceOrCreateTable(_logger, ex);
                DisableService();
                Logger.ServiceDisabledUnauthorizedClient(_logger, ex);
                return;
            }
            catch (Exception ex)
            {
                // We failed to connect to the table storage account. This could be due to a transient error or a configuration issue (e.g., network problems).
                // To avoid repeatedly retrying in a potentially unhealthy state, we will disable the service.
                // The operation may succeed in a future instance if the underlying issue is resolved.
                Logger.UnableToGetTableReferenceOrCreateTable(_logger, ex);
                DisableService();
                Logger.ServiceDisabledUnableToConnectToStorage(_logger, ex);
                return;
            }

            // Swap the events dictionary to reset the event count for the new flush window.
            // All existing events are logged to other logging pipelines already.
            // Use Interlocked.Exchange to atomically swap dictionaries while preventing race conditions.
            var tempDictionary = Interlocked.Exchange(ref _events, new ConcurrentDictionary<string, DiagnosticEvent>());

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
                DisableService();
                Logger.ServiceDisabledUnableToConnectToStorage(_logger, ex);
            }
        }

        public void WriteDiagnosticEvent(DateTime timestamp, string errorCode, LogLevel level, string message, string helpLink, Exception exception)
        {
            if (!IsEnabled() || string.IsNullOrEmpty(HostId))
            {
                return;
            }

            // If the table client hasn't been initialized yet, kick off initialization.
            // This handles the case where the host was in placeholder mode during construction
            // and has since specialized — the constructor skipped initialization, so we trigger it here.
            // Errors are handled within InitializeTableClientAsync, which disables the service on failure.
            if (!_tableClientInitialized)
            {
                _ = InitializeTableClientAsync();
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

            // Use AddOrUpdate for atomic add-or-update operation.
            // ConcurrentDictionary ensures thread-safety for this operation.
            // If the dictionary is swapped by FlushLogs during this call, the event will be added to whichever
            // dictionary the reference points to at the time. Events added to the old dictionary after the swap
            // will still be flushed in the current cycle.
            _events.AddOrUpdate(
                errorCode,
                diagnosticEvent,
                (key, existingEvent) =>
                {
                    existingEvent.Timestamp = timestamp;
                    existingEvent.IncrementHitCount();
                    return existingEvent;
                });

            EnsureFlushLogsTimerInitialized();
        }

        internal void EnsureFlushLogsTimerInitialized()
        {
            if (_disposed || !IsEnabled())
            {
                return;
            }

            _ = _flushLogsTimer.Value;
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
            if (!_flushLogsTimer.IsValueCreated)
            {
                return;
            }

            Logger.StoppingFlushLogsTimer(_logger);
            _flushLogsTimer?.Value?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;

                if (disposing)
                {
                    if (_flushLogsTimer.IsValueCreated)
                    {
                        _flushLogsTimer.Value?.Dispose();
                    }

                    if (_tableClient is not null)
                    {
                        FlushLogs().GetAwaiter().GetResult();
                    }

                    if (_initSemaphore is not null)
                    {
                        try
                        {
                            _initSemaphore.Dispose();
                        }
                        catch (ObjectDisposedException)
                        {
                            // Suppress exception if semaphore was already disposed or is being disposed by another thread
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}