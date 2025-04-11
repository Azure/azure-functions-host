// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public partial class DiagnosticEventTableStorageRepository
    {
        private static class Logger
        {
            private static readonly Action<ILogger, Exception> _serviceDisabledFailedToCreateClient =
                LoggerMessage.Define(LogLevel.Warning, new EventId(1, nameof(ServiceDisabledFailedToCreateClient)), "An error occurred initializing the Table Storage Client. We are unable to record diagnostic events, so the diagnostic logging service is being stopped.");

            private static readonly Action<ILogger, Exception> _serviceDisabledUnauthorizedClient =
                LoggerMessage.Define(LogLevel.Warning, new EventId(2, nameof(ServiceDisabledUnauthorizedClient)), "The Table Storage Client is not authorized to access the table storage account. We are unable to record diagnostic events, so the diagnostic logging service is being stopped.");

            private static readonly Action<ILogger, string, Exception> _purgingDiagnosticEvents =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(3, nameof(PurgingDiagnosticEvents)), "Purging diagnostic events with versions older than '{currentEventVersion}'.");

            private static readonly Action<ILogger, string, Exception> _deletingTableWithoutEventVersion =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(4, nameof(DeletingTableWithoutEventVersion)), "Deleting table '{tableName}' as it contains records without an EventVersion.");

            private static readonly Action<ILogger, string, Exception> _deletingTableWithOutdatedEventVersion =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(5, nameof(DeletingTableWithOutdatedEventVersion)), "Deleting table '{tableName}' as it contains records with an outdated EventVersion.");

            private static readonly Action<ILogger, Exception> _errorPurgingDiagnosticEventVersions =
                LoggerMessage.Define(LogLevel.Error, new EventId(6, nameof(ErrorPurgingDiagnosticEventVersions)), "Error occurred when attempting to purge previous diagnostic event versions.");

            private static readonly Action<ILogger, Exception> _unableToGetTableReference =
                LoggerMessage.Define(LogLevel.Error, new EventId(7, nameof(UnableToGetTableReference)), "Unable to get table reference. Aborting write operation.");

            private static readonly Action<ILogger, Exception> _unableToGetTableReferenceOrCreateTable =
                LoggerMessage.Define(LogLevel.Error, new EventId(8, nameof(UnableToGetTableReferenceOrCreateTable)), "Unable to get table reference or create table. Aborting write operation.");

            private static readonly Action<ILogger, Exception> _unableToWriteDiagnosticEvents =
                LoggerMessage.Define(LogLevel.Error, new EventId(9, nameof(UnableToWriteDiagnosticEvents)), "Unable to write diagnostic events to table storage.");

            private static readonly Action<ILogger, Exception> _primaryHostStateProviderNotAvailable =
                LoggerMessage.Define(LogLevel.Debug, new EventId(10, nameof(PrimaryHostStateProviderNotAvailable)), "PrimaryHostStateProvider is not available. Skipping the check for primary host.");

            private static readonly Action<ILogger, Exception> _stoppingFlushLogsTimer =
                LoggerMessage.Define(LogLevel.Information, new EventId(11, nameof(StoppingFlushLogsTimer)), "Stopping the flush logs timer.");

            private static readonly Action<ILogger, Exception> _queueingBackgroundTablePurge =
                LoggerMessage.Define(LogLevel.Debug, new EventId(12, nameof(QueueingBackgroundTablePurge)), "Queueing background table purge.");

            public static void ServiceDisabledFailedToCreateClient(ILogger logger) => _serviceDisabledFailedToCreateClient(logger, null);

            public static void ServiceDisabledUnauthorizedClient(ILogger logger, Exception exception) => _serviceDisabledUnauthorizedClient(logger, exception);

            public static void PurgingDiagnosticEvents(ILogger logger, string currentEventVersion) => _purgingDiagnosticEvents(logger, currentEventVersion, null);

            public static void DeletingTableWithoutEventVersion(ILogger logger, string tableName) => _deletingTableWithoutEventVersion(logger, tableName, null);

            public static void DeletingTableWithOutdatedEventVersion(ILogger logger, string tableName) => _deletingTableWithOutdatedEventVersion(logger, tableName, null);

            public static void ErrorPurgingDiagnosticEventVersions(ILogger<DiagnosticEventTableStorageRepository> logger, Exception exception) => _errorPurgingDiagnosticEventVersions(logger, exception);

            public static void UnableToGetTableReference(ILogger logger) => _unableToGetTableReference(logger, null);

            public static void UnableToGetTableReferenceOrCreateTable(ILogger logger, Exception exception) => _unableToGetTableReferenceOrCreateTable(logger, exception);

            public static void UnableToWriteDiagnosticEvents(ILogger logger, Exception exception) => _unableToWriteDiagnosticEvents(logger, exception);

            public static void PrimaryHostStateProviderNotAvailable(ILogger logger) => _primaryHostStateProviderNotAvailable(logger, null);

            public static void StoppingFlushLogsTimer(ILogger logger) => _stoppingFlushLogsTimer(logger, null);

            public static void QueueingBackgroundTablePurge(ILogger logger) => _queueingBackgroundTablePurge(logger, null);
        }
    }
}
