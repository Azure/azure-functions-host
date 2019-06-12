// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
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

        private static readonly Action<ILogger, string, Exception> _startingHost =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(401, nameof(StartingHost)),
            "{message}");

        private static readonly Action<ILogger, string, Exception> _functionsErrors =
            LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(402, nameof(FunctionsErrors)),
            "{message}");

        private static readonly Action<ILogger, Exception> _addingDescriptorProvidersForAllLanguages =
            LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(403, nameof(AddingDescriptorProvidersForAllLanguages)),
            "Adding Function descriptor providers for all languages.");

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

        public static void HostIdIsSet(this ILogger logger)
        {
            _hostIdIsSet(logger, null);
        }

        public static void StartingHost(this ILogger logger, string hostId, string instanceId, string version, bool inDebugMode, bool inDiagnosticMode, string extensionVersion)
        {
            string message = $"Starting Host (HostId={hostId}, InstanceId={instanceId}, Version={version}, ProcessId={Process.GetCurrentProcess().Id}, AppDomainId={AppDomain.CurrentDomain.Id}, InDebugMode={inDebugMode}, InDiagnosticMode={inDiagnosticMode}, FunctionsExtensionVersion={extensionVersion})";
            _startingHost(logger, message, null);
        }

        public static void FunctionsErrors(this ILogger logger, string message)
        {
            _functionsErrors(logger, message, null);
        }

        public static void AddingDescriptorProvidersForAllLanguages(this ILogger logger)
        {
            _addingDescriptorProvidersForAllLanguages(logger, null);
        }

        public static void AddingDescriptorProviderForLanguage(this ILogger logger, string workerRuntime)
        {
            _addingDescriptorProviderForLanguage(logger, workerRuntime, null);
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
    }
}
