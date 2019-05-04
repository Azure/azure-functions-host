// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;
using static System.Environment;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class SemanticLogger
    {
        public static void HostConfigApplied(ILogger logger)
        {
            logger.Log(LogLevel.Debug, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "HostConfigApplied" },
                null, (s, e) => "Host configuration applied.");
        }

        public static void HostConfigReading(ILogger logger, string hostFilePath)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "HostConfigReading" },
                null, (s, e) => $"Reading host configuration file '{hostFilePath}'");
        }

        public static void HostConfigRead(ILogger logger, string sanitizedJson)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "HostConfigRead" },
                null, (s, e) => $"Host configuration file read:{NewLine}{sanitizedJson}");
        }

        public static void HostConfigEmpty(ILogger logger)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "HostConfigEmpty" },
                null, (s, e) => $"Empty host configuration file found. Creating a default {ScriptConstants.HostMetadataFileName} file.");
        }

        public static void HostConfigNotFound(ILogger logger)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "HostConfigNotFound" },
                null, (s, e) => $"No host configuration file found. Creating a default {ScriptConstants.HostMetadataFileName} file.");
        }

        public static void HostConfigCreationFailed(ILogger logger)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "HostConfigCreationFailed" },
                null, (s, e) => $"Failed to create {ScriptConstants.HostMetadataFileName} file. Host execution will continue.");
        }

        public static void HostConfigFileSystemReadOnly(ILogger logger)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "HostConfigFileSystemReadOnly" },
                null, (s, e) => $"File system is read-only. Skipping {ScriptConstants.HostMetadataFileName} creation.");
        }

        public static void ScriptHostInitCanceledByRuntime(ILogger logger)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostInitCanceledByRuntime" },
                null, (s, e) => "Initialization cancellation requested by runtime.");
        }

        public static void ScriptHostUnhealthyCountExceeded(ILogger logger, int healthCheckThreshold, TimeSpan healthCheckWindow)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostUnhealthyCountExceeded" },
                null, (s, e) => $"Host unhealthy count exceeds the threshold of {healthCheckThreshold} for time window {healthCheckWindow}. Initiating shutdown.");
        }

        public static void ScriptHostOffline(ILogger logger)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostOffline" },
                null, (s, e) => "Host is offline.");
        }

        public static void ScriptHostInitializing(ILogger logger)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostInitializing" },
                null, (s, e) => "Initializing Host.");
        }

        public static void ScriptHostInitialization(ILogger logger, int attemptCount, int startCount)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostInitialization" },
                null, (s, e) => $"Host initialization: ConsecutiveErrors={attemptCount}, StartupCount={startCount}");
        }

        public static void ScriptHostInStandByMode(ILogger logger)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostInStandByMode" },
                null, (s, e) => "Host is in standby mode");
        }

        public static void ScriptHostUnhealthyRestart(ILogger logger)
        {
            logger.Log(LogLevel.Error, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostUnhealthyRestart" },
                null, (s, e) => "Host is unhealthy. Initiating a restart.");
        }

        public static void ScriptHostStopping(ILogger logger)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostStopping" },
                null, (s, e) => "Stopping host...");
        }

        public static void ScriptHostDidNotShutDown(ILogger logger)
        {
            logger.Log(LogLevel.Warning, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostDidNotShutDown" },
                null, (s, e) => "Host did not shutdown within its allotted time.");
        }

        public static void ScriptHostShutDoewnCompleted(ILogger logger)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostShutDoewnCompleted" },
                null, (s, e) => "Host shutdown completed.");
        }

        public static void ScriptHostSkipRestart(ILogger logger, string state)
        {
            logger.Log(LogLevel.Debug, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostSkipRestart" },
                null, (s, e) => "Restarting host.");
        }

        public static void ScriptHostRestarting(ILogger logger)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostRestarting" },
                null, (s, e) => "Restarting host.");
        }

        public static void ScriptHostRestarted(ILogger logger)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostRestarted" },
                null, (s, e) => "Host restarted.");
        }

        public static void ScriptHostBuilding(ILogger logger, bool skipHostStartup, bool skipHostJsonConfiguration)
        {
            logger.Log(LogLevel.Information, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostBuilding" },
                null, (s, e) => $"Building host: startup suppressed:{skipHostStartup}, configuration suppressed: {skipHostJsonConfiguration}");
        }

        public static void ScriptHostStartupWasCanceled(ILogger logger)
        {
            logger.Log(LogLevel.Debug, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostStartupWasCanceled" },
                null, (s, e) => "Host startup was canceled.");
        }

        public static void ScriptHostErrorOccured(ILogger logger, Exception ex)
        {
            logger.Log(LogLevel.Debug, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostErrorOccured" },
                ex, (s, e) => "A host error has occurred");
        }

        public static void ScriptHostErrorOccuredInactive(ILogger logger, Exception ex)
        {
            logger.Log(LogLevel.Debug, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostErrorOccuredInactive" },
                null, (s, e) => "A host error has occurred on an inactive host");
        }

        public static void ScriptHostCancellationRequested(ILogger logger)
        {
            logger.Log(LogLevel.Debug, 0, new Dictionary<string, object> { [ScriptConstants.LogPropertyEventNameKey] = "ScriptHostCancellationRequested" },
                null, (s, e) => "Cancellation requested. A new host will not be started.");
        }
    }
}
