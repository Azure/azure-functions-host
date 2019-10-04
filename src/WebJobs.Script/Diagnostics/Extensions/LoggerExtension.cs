// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions
{
    internal static class LoggerExtension
    {
        // EventId range is 300-399

        private static readonly Action<ILogger, Exception> _extensionsManagerRestoring =
            LoggerMessage.Define(
            LogLevel.Information,
            new EventId(300, nameof(ExtensionsManagerRestoring)),
            "Restoring extension packages");

        private static readonly Action<ILogger, Exception> _extensionsManagerRestoreSucceeded =
            LoggerMessage.Define(
            LogLevel.Information,
            new EventId(301, nameof(ExtensionsManagerRestoreSucceeded)),
            "Extensions packages restore succeeded.'");

        private static readonly Action<ILogger, Exception> _scriptStartUpErrorLoadingExtensionBundle =
            LoggerMessage.Define(
            LogLevel.Error,
            new EventId(302, nameof(ScriptStartUpErrorLoadingExtensionBundle)),
            "Unable to find or download extension bundle");

        private static readonly Action<ILogger, string, Exception> _scriptStartUpLoadingExtensionBundle =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(303, nameof(ScriptStartUpLoadingExtensionBundle)),
            "Loading Extention bundle from {path}");

        private static readonly Action<ILogger, string, Exception> _scriptStartUpLoadingStartUpExtension =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(304, nameof(ScriptStartUpLoadingStartUpExtension)),
            "Loading startup extension '{startupExtensionName}'");

        private static readonly Action<ILogger, string, Exception> _scriptStartUpBelongExtension =
            LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(305, nameof(ScriptStartUpBelongExtension)),
            "The extension startup type '{typeName}' belongs to a builtin extension");

        private static readonly Action<ILogger, string, string, Exception> _scriptStartUpUnableToLoadExtension =
            LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(306, nameof(ScriptStartUpUnableToLoadExtension)),
            "Unable to load startup extension '{startupExtensionName}' (Type: '{typeName}'). The type does not exist. Please validate the type and assembly names.");

        private static readonly Action<ILogger, string, string, Exception> _scriptStartUpTypeIsNotValid =
            LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(307, nameof(ScriptStartUpTypeIsNotValid)),
            "Type '{typeName}' is not a valid startup extension. The type does not implement {className}.");

        private static readonly Action<ILogger, string, Exception> _scriptStartUpUnableParseMetadataMissingProperty =
            LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(308, nameof(ScriptStartUpUnableParseMetadataMissingProperty)),
            "Unable to parse extensions metadata file '{metadataFilePath}'. Missing 'extensions' property.");

        private static readonly Action<ILogger, string, Exception> _scriptStartUpUnableParseMetadata =
            LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(309, nameof(ScriptStartUpUnableParseMetadata)),
            "Unable to parse extensions metadata file '{metadataFilePath}'");

        private static readonly Action<ILogger, Exception> _packageManagerStartingPackagesRestore =
            LoggerMessage.Define(
            LogLevel.Information,
            new EventId(310, nameof(PackageManagerStartingPackagesRestore)),
            "Starting packages restore");

        private static readonly Action<ILogger, string, Exception> _packageManagerRestoreFailed =
            LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(311, nameof(PackageManagerRestoreFailed)),
            "{message}");

        private static readonly Action<ILogger, string, Exception> _packageManagerProcessDataReceived =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(312, nameof(PackageManagerProcessDataReceived)),
            "{message}");

        private static readonly Action<ILogger, Exception> _debugManagerUnableToUpdateSentinelFile =
            LoggerMessage.Define(
            LogLevel.Error,
            new EventId(313, nameof(DebugManagerUnableToUpdateSentinelFile)),
            "Unable to update the debug sentinel file.");

        private static readonly Action<ILogger, Exception> _functionMetadataManagerLoadingFunctionsMetadata =
            LoggerMessage.Define(
            LogLevel.Information,
            new EventId(314, nameof(FunctionMetadataManagerLoadingFunctionsMetadata)),
            "Loading functions metadata");

        private static readonly Action<ILogger, int, Exception> _functionMetadataManagerFunctionsLoaded =
            LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(315, nameof(FunctionMetadataManagerFunctionsLoaded)),
            "{count} functions loaded");

        private static readonly Action<ILogger, string, Exception> _primaryHostCoordinatorLockLeaseAcquired =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(316, nameof(PrimaryHostCoordinatorLockLeaseAcquired)),
            "Host lock lease acquired by instance ID '{websiteInstanceId}'.");

        private static readonly Action<ILogger, string, Exception> _primaryHostCoordinatorFailedToRenewLockLease =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(317, nameof(PrimaryHostCoordinatorFailedToRenewLockLease)),
            "Failed to renew host lock lease: {reason}");

        private static readonly Action<ILogger, string, string, Exception> _primaryHostCoordinatorFailedToAcquireLockLease =
            LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(318, nameof(PrimaryHostCoordinatorFailedToAcquireLockLease)),
            "Host instance '{websiteInstanceId}' failed to acquire host lock lease: {reason}");

        private static readonly Action<ILogger, string, Exception> _primaryHostCoordinatorReleasedLocklLease =
            LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(319, nameof(PrimaryHostCoordinatorReleasedLocklLease)),
            "Host instance '{websiteInstanceId}' released lock lease.");

        private static readonly Action<ILogger, string, string, Exception> _autoRecoveringFileSystemWatcherFailureDetected =
            LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(320, nameof(AutoRecoveringFileSystemWatcherFailureDetected)),
            "Failure detected '{errorMessage}'. Initiating recovery... (path: '{path}')");

        private static readonly Action<ILogger, string, Exception> _autoRecoveringFileSystemWatcherRecoveryAborted =
            LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(321, nameof(AutoRecoveringFileSystemWatcherRecoveryAborted)),
            "Recovery process aborted. (path: '{path}')");

        private static readonly Action<ILogger, string, Exception> _autoRecoveringFileSystemWatcherRecovered =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(322, nameof(AutoRecoveringFileSystemWatcherRecovered)),
            "File watcher recovered. (path: '{path}')");

        private static readonly Action<ILogger, string, Exception> _autoRecoveringFileSystemWatcherAttemptingToRecover =
            LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(323, nameof(AutoRecoveringFileSystemWatcherAttemptingToRecover)),
            "Attempting to recover... (path: '{path}')");

        private static readonly Action<ILogger, string, Exception> _autoRecoveringFileSystemWatcherUnableToRecover =
            LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(324, nameof(AutoRecoveringFileSystemWatcherUnableToRecover)),
            "Unable to recover (path: '{path}')");

        private static readonly Action<ILogger, string, string, Exception> _scriptStartUpLoadedExtension =
            LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(325, nameof(ScriptStartUpLoadedExtension)),
            "Loaded extension '{startupExtensionName}' ({startupExtensionVersion})");

        private static readonly Action<ILogger, Exception> _functionMetadataProviderReadingMetadata =
            LoggerMessage.Define(
            LogLevel.Information,
            new EventId(326, nameof(FunctionMetadataManagerLoadingFunctionsMetadata)),
            "Reading functions metadata");

        private static readonly Action<ILogger, int, Exception> _functionMetadataProviderFunctionsFound =
            LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(327, nameof(FunctionMetadataManagerFunctionsLoaded)),
            "{count} functions found");

        public static void ExtensionsManagerRestoring(this ILogger logger)
        {
            _extensionsManagerRestoring(logger, null);
        }

        public static void ExtensionsManagerRestoreSucceeded(this ILogger logger)
        {
            _extensionsManagerRestoreSucceeded(logger, null);
        }

        public static void ScriptStartUpErrorLoadingExtensionBundle(this ILogger logger)
        {
            _scriptStartUpErrorLoadingExtensionBundle(logger, null);
        }

        public static void ScriptStartUpLoadingExtensionBundle(this ILogger logger, string path)
        {
            _scriptStartUpLoadingExtensionBundle(logger, path, null);
        }

        public static void ScriptStartUpLoadingStartUpExtension(this ILogger logger, string startupExtensionName)
        {
            _scriptStartUpLoadingStartUpExtension(logger, startupExtensionName, null);
        }

        public static void ScriptStartUpLoadedExtension(this ILogger logger, string startupExtensionName, string startupExtensionVersion)
        {
            _scriptStartUpLoadedExtension(logger, startupExtensionName, startupExtensionVersion, null);
        }

        public static void ScriptStartUpBelongExtension(this ILogger logger, string typeName)
        {
            _scriptStartUpBelongExtension(logger, typeName, null);
        }

        public static void ScriptStartUpUnableToLoadExtension(this ILogger logger, string startupExtensionName, string typeName)
        {
            _scriptStartUpUnableToLoadExtension(logger, startupExtensionName, typeName, null);
        }

        public static void ScriptStartUpTypeIsNotValid(this ILogger logger, string typeName, string className)
        {
            _scriptStartUpTypeIsNotValid(logger, typeName, className, null);
        }

        public static void ScriptStartUpUnableParseMetadataMissingProperty(this ILogger logger, string metadataFilePath)
        {
            _scriptStartUpUnableParseMetadataMissingProperty(logger, metadataFilePath, null);
        }

        public static void ScriptStartUpUnableParseMetadata(this ILogger logger, Exception ex, string metadataFilePath)
        {
            _scriptStartUpUnableParseMetadata(logger, metadataFilePath, null);
        }

        public static void PackageManagerStartingPackagesRestore(this ILogger logger)
        {
            _packageManagerStartingPackagesRestore(logger, null);
        }

        public static void PackageManagerRestoreFailed(this ILogger logger, Exception ex, string functionDirectory, string projectPath, string nugetHome, string nugetFilePath, string currentLockFileHash)
        {
            string message = $@"Package restore failed with message: '{ex.Message}'
Function directory: {functionDirectory}
Project path: {projectPath}
Packages path: {nugetHome}
Nuget client path: {nugetFilePath}
Lock file hash: {currentLockFileHash}";

            _packageManagerRestoreFailed(logger, message, null);
        }

        public static void PackageManagerProcessDataReceived(this ILogger logger, string message)
        {
            _packageManagerProcessDataReceived(logger, message, null);
        }

        public static void DebugManagerUnableToUpdateSentinelFile(this ILogger logger, Exception ex)
        {
            _debugManagerUnableToUpdateSentinelFile(logger, ex);
        }

        public static void FunctionMetadataManagerLoadingFunctionsMetadata(this ILogger logger)
        {
            _functionMetadataManagerLoadingFunctionsMetadata(logger, null);
        }

        public static void FunctionMetadataManagerFunctionsLoaded(this ILogger logger, int count)
        {
            _functionMetadataManagerFunctionsLoaded(logger, count, null);
        }

        public static void FunctionMetadataProviderParsingFunctions(this ILogger logger)
        {
            _functionMetadataProviderReadingMetadata(logger, null);
        }

        public static void FunctionMetadataProviderFunctionFound(this ILogger logger, int count)
        {
            _functionMetadataProviderFunctionsFound(logger, count, null);
        }

        public static void PrimaryHostCoordinatorLockLeaseAcquired(this ILogger logger, string websiteInstanceId)
        {
            _primaryHostCoordinatorLockLeaseAcquired(logger, websiteInstanceId, null);
        }

        public static void PrimaryHostCoordinatorFailedToRenewLockLease(this ILogger logger, string reason)
        {
            _primaryHostCoordinatorFailedToRenewLockLease(logger, reason, null);
        }

        public static void PrimaryHostCoordinatorFailedToAcquireLockLease(this ILogger logger, string websiteInstanceId, string reason)
        {
            _primaryHostCoordinatorFailedToAcquireLockLease(logger, websiteInstanceId, reason, null);
        }

        public static void PrimaryHostCoordinatorReleasedLocklLease(this ILogger logger, string websiteInstanceId)
        {
            _primaryHostCoordinatorReleasedLocklLease(logger, websiteInstanceId, null);
        }

        public static void AutoRecoveringFileSystemWatcherFailureDetected(this ILogger logger, string errorMessage, string path)
        {
            _autoRecoveringFileSystemWatcherFailureDetected(logger, errorMessage, path, null);
        }

        public static void AutoRecoveringFileSystemWatcherRecoveryAborted(this ILogger logger, string path)
        {
            _autoRecoveringFileSystemWatcherRecoveryAborted(logger, path, null);
        }

        public static void AutoRecoveringFileSystemWatcherRecovered(this ILogger logger, string path)
        {
            _autoRecoveringFileSystemWatcherRecovered(logger, path, null);
        }

        public static void AutoRecoveringFileSystemWatcherAttemptingToRecover(this ILogger logger, string path)
        {
            _autoRecoveringFileSystemWatcherAttemptingToRecover(logger, path, null);
        }

        public static void AutoRecoveringFileSystemWatcherUnableToRecover(this ILogger logger, Exception ex, string path)
        {
            _autoRecoveringFileSystemWatcherUnableToRecover(logger, path, ex);
        }
    }
}
