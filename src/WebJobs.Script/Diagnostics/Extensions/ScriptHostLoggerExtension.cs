// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions
{
    internal static class ScriptHostLoggerExtension
    {
        // EventId range is 400-499

        private static readonly Action<ILogger, Exception> _hostIdIsSet =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(400, nameof(HostIdIsSet)),
                "Host id explicitly set in configuration. This is not a recommended configuration and may lead to unexpected behavior.");

        private static readonly Action<ILogger, string, string, Exception> _functionError =
            LoggerMessage.Define<string, string>(
                LogLevel.Error,
                new EventId(402, nameof(FunctionError)),
                "The '{functionName}' function is in error: {errorMessage}");

        private static readonly Action<ILogger, Exception> _hostIsInPlaceholderMode =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(403, nameof(HostIsInPlaceholderMode)),
                "Host is in placeholdermode");

        private static readonly Action<ILogger, string, Exception> _addingDescriptorProviderForLanguage =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(404, nameof(AddingDescriptorProviderForLanguage)),
                "Adding Function descriptor provider for language {workerRuntime}.");

        private static readonly Action<ILogger, Exception> _creatingDescriptors =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(405, nameof(CreatingDescriptors)),
                "Creating function descriptors.");

        private static readonly Action<ILogger, Exception> _descriptorsCreated =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(406, nameof(DescriptorsCreated)),
                "Function descriptors created.");

        private static readonly Action<ILogger, Exception> _errorPurgingLogFiles =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(407, nameof(ErrorPurgingLogFiles)),
                "An error occurred while purging log files");

        private static readonly Action<ILogger, string, Exception> _deletingLogDirectory =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(408, nameof(DeletingLogDirectory)),
                "Deleting log directory '{logDir}'");

        private static readonly Action<ILogger, string, string, Exception> _failedToLoadType =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(409, nameof(FailedToLoadType)),
                "Failed to load type '{typeName}' from '{path}'");

        private static readonly Action<ILogger, string, Exception> _configurationError =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(410, nameof(ConfigurationError)),
                "{message}");

        private static readonly Action<ILogger, string, string, Exception> _versionRecommendation =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(411, nameof(VersionRecommendation)),
                "Site extension version currently set to '{extensionVersion}'. It is recommended that you target a major version (e.g. ~2) to avoid unintended upgrades. You can change that value by updating the '{settingName}' App Setting.");

        private static readonly Action<ILogger, long, Exception> _scriptHostInitialized =
            LoggerMessage.Define<long>(
                LogLevel.Information,
                new EventId(412, nameof(ScriptHostInitialized)),
                "Host initialized ({ms}ms)");

        private static readonly Action<ILogger, long, Exception> _scriptHostStarted =
            LoggerMessage.Define<long>(
                LogLevel.Information,
                new EventId(413, nameof(ScriptHostStarted)),
                "Host started ({ms}ms)");

        private static readonly Action<ILogger, Exception> _addingDescriptorProviderForHttpWorker =
           LoggerMessage.Define(
               LogLevel.Debug,
               new EventId(414, nameof(AddingDescriptorProviderForHttpWorker)),
               "Adding Function descriptor provider for HttpWorker.");

        private static readonly Action<ILogger, string, Exception> _stoppingScriptHost =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(415, nameof(StoppingScriptHost)),
                "Stopping ScriptHost instance '{hostInstanceId}'.");

        private static readonly Action<ILogger, string, Exception> _stoppedScriptHost =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(416, nameof(StoppedScriptHost)),
                "Stopped ScriptHost instance '{hostInstanceId}'.");

        private static readonly Action<ILogger, string, Exception> _disposingScriptHost =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(417, nameof(DisposingScriptHost)),
                "Disposing ScriptHost instance '{hostInstanceId}'.");

        private static readonly Action<ILogger, string, Exception> _disposedScriptHost =
            LoggerMessage.Define<string>(
              LogLevel.Debug,
              new EventId(418, nameof(DisposedScriptHost)),
              "Disposed ScriptHost instance '{hostInstanceId}'.");

        private static readonly Action<ILogger, string, Exception> _functionsWorkerRuntimeValue =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(419, nameof(FunctionsWorkerRuntimeValue)),
                $"{EnvironmentSettingNames.FunctionWorkerRuntime} value: '{{workerRuntime}}'");

        private static readonly Action<ILogger, string, Exception> _resolvedWorkerRuntimeFromMetadata =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(420, nameof(ResolvedWorkerRuntimeFromMetadata)),
                $"{EnvironmentSettingNames.FunctionWorkerRuntime} is null. Resolved worker runtime from function metadata: '{{workerRuntime}}'");

        public static void HostIdIsSet(this ILogger logger)
        {
            _hostIdIsSet(logger, null);
        }

        public static void StartingHost(this ILogger logger, string hostId, string instanceId, string version, int processId, int appDomainId, bool inDebugMode, bool inDiagnosticMode, string extensionVersion)
        {
            // LoggerMessage.Define can only handle a max of 6 parameters, so log this directly.
            logger.LogInformation(new EventId(401, "StartingHost"),
                "Starting Host (HostId={hostId}, InstanceId={instanceId}, Version={version}, ProcessId={processId}, AppDomainId={appDomainId}, InDebugMode={inDebugMode}, InDiagnosticMode={inDiagnosticMode}, FunctionsExtensionVersion={extensionVersion})",
                hostId, instanceId, version, processId, appDomainId, inDebugMode, inDiagnosticMode, extensionVersion);
        }

        public static void FunctionError(this ILogger logger, string functionName, string errorMessage)
        {
            _functionError(logger, functionName, errorMessage, null);
        }

        public static void HostIsInPlaceholderMode(this ILogger logger)
        {
            _hostIsInPlaceholderMode(logger, null);
        }

        public static void AddingDescriptorProviderForLanguage(this ILogger logger, string workerRuntime)
        {
            _addingDescriptorProviderForLanguage(logger, workerRuntime, null);
        }

        public static void AddingDescriptorProviderForHttpWorker(this ILogger logger)
        {
            _addingDescriptorProviderForHttpWorker(logger, null);
        }

        public static void CreatingDescriptors(this ILogger logger)
        {
            _creatingDescriptors(logger, null);
        }

        public static void DescriptorsCreated(this ILogger logger)
        {
            _descriptorsCreated(logger, null);
        }

        public static void ErrorPurgingLogFiles(this ILogger logger, Exception ex)
        {
            _errorPurgingLogFiles(logger, ex);
        }

        public static void DeletingLogDirectory(this ILogger logger, string logDir)
        {
            _deletingLogDirectory(logger, logDir, null);
        }

        public static void FailedToLoadType(this ILogger logger, string typeName, string path)
        {
            _failedToLoadType(logger, typeName, path, null);
        }

        public static void ConfigurationError(this ILogger logger, string message)
        {
            _configurationError(logger, message, null);
        }

        public static void VersionRecommendation(this ILogger logger, string extensionVersion)
        {
            _versionRecommendation(logger, extensionVersion, EnvironmentSettingNames.FunctionsExtensionVersion, null);
        }

        public static void ScriptHostInitialized(this ILogger logger, long ms)
        {
            _scriptHostInitialized(logger, ms, null);
        }

        public static void ScriptHostStarted(this ILogger logger, long ms)
        {
            _scriptHostStarted(logger, ms, null);
        }

        public static void StoppingScriptHost(this ILogger logger, string hostInstanceId)
        {
            _stoppingScriptHost(logger, hostInstanceId, null);
        }

        public static void StoppedScriptHost(this ILogger logger, string hostInstanceId)
        {
            _stoppedScriptHost(logger, hostInstanceId, null);
        }

        public static void DisposingScriptHost(this ILogger logger, string hostInstanceId)
        {
            _disposingScriptHost(logger, hostInstanceId, null);
        }

        public static void DisposedScriptHost(this ILogger logger, string hostInstanceId)
        {
            _disposedScriptHost(logger, hostInstanceId, null);
        }

        public static void FunctionsWorkerRuntimeValue(this ILogger logger, string workerRuntime)
        {
            _functionsWorkerRuntimeValue(logger, workerRuntime, null);
        }

        public static void ResolvedWorkerRuntimeFromMetadata(this ILogger logger, string workerRuntime)
        {
            _resolvedWorkerRuntimeFromMetadata(logger, workerRuntime, null);
        }
    }
}
