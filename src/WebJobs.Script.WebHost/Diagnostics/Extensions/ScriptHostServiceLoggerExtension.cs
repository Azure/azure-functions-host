// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions
{
    public static class ScriptHostServiceLoggerExtension
    {
        // EventId range is 500-599

        private static readonly Action<ILogger, Exception> _scriptHostServiceInitCanceledByRuntime =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(500, nameof(ScriptHostServiceInitCanceledByRuntime)),
                "Initialization cancellation requested by runtime.");

        private static readonly Action<ILogger, int, TimeSpan, Exception> _unehealthyCountExceeded =
            LoggerMessage.Define<int, TimeSpan>(
                LogLevel.Error,
                new EventId(501, nameof(UnhealthyCountExceeded)),
                "Host unhealthy count exceeds the threshold of {healthCheckThreshold} for time window {healthCheckWindow}. Initiating shutdown.");

        private static readonly Action<ILogger, Exception> _offline =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(502, nameof(Offline)),
                "Host is offline.");

        private static readonly Action<ILogger, Exception> _initializing =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(503, nameof(Initializing)),
                "Initializing Host.");

        private static readonly Action<ILogger, int, int, Exception> _initialization =
            LoggerMessage.Define<int, int>(
                LogLevel.Information,
                new EventId(504, nameof(Initialization)),
                "Host initialization: ConsecutiveErrors={attemptCount}, StartupCount={startCount}");

        private static readonly Action<ILogger, Exception> _inStandByMode =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(505, nameof(InStandByMode)),
                "Host is in standby mode");

        private static readonly Action<ILogger, Exception> _unhealthyRestart =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(506, nameof(UnhealthyRestart)),
                "Host is unhealthy. Initiating a restart.");

        private static readonly Action<ILogger, Exception> _stopping =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(507, nameof(Stopping)),
                "Stopping host...");

        private static readonly Action<ILogger, Exception> _didNotShutDown =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(508, nameof(DidNotShutDown)),
                "Host did not shutdown within its allotted time.");

        private static readonly Action<ILogger, Exception> _shutDownCompleted =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(509, nameof(ShutDownCompleted)),
                "Host shutdown completed.");

        private static readonly Action<ILogger, string, Exception> _skipRestart =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(510, nameof(SkipRestart)),
                "Host restart was requested, but current host state is '{state}'. Skipping restart.");

        private static readonly Action<ILogger, Exception> _restarting =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(511, nameof(Restarting)),
                "Restarting host.");

        private static readonly Action<ILogger, Exception> _restarted =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(512, nameof(Restarted)),
                "Host restarted.");

        private static readonly Action<ILogger, string, string, Exception> _building =
            LoggerMessage.Define<string, string>(
                LogLevel.Information,
                new EventId(513, nameof(Building)),
                "Building host: startup suppressed:{skipHostStartup}, configuration suppressed: {skipHostJsonConfiguration}");

        private static readonly Action<ILogger, Exception> _startupWasCanceled =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(514, nameof(StartupWasCanceled)),
                "Host startup was canceled.");

        private static readonly Action<ILogger, Exception> _errorOccured =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(515, nameof(ErrorOccured)),
                "A host error has occurred");

        private static readonly Action<ILogger, Exception> _errorOccuredInactive =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(516, nameof(ErrorOccuredInactive)),
                "A host error has occurred on an inactive host");

        private static readonly Action<ILogger, Exception> _cancellationRequested =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(517, nameof(CancellationRequested)),
                "Cancellation requested. A new host will not be started.");

        private static readonly Action<ILogger, string, string, Exception> _activeHostChanging =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug,
                new EventId(518, nameof(ActiveHostChanging)),
                "Active host changing from '{oldHostInstanceId}' to '{newHostInstanceId}'.");

        private static readonly Action<ILogger, Exception> _enteringRestart =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(519, nameof(EnteringRestart)),
                "Restart requested. Cancelling any active host startup.");

        public static void ScriptHostServiceInitCanceledByRuntime(this ILogger logger)
        {
            _scriptHostServiceInitCanceledByRuntime(logger, null);
        }

        public static void UnhealthyCountExceeded(this ILogger logger, int healthCheckThreshold, TimeSpan healthCheckWindow)
        {
            _unehealthyCountExceeded(logger, healthCheckThreshold, healthCheckWindow, null);
        }

        public static void Offline(this ILogger logger)
        {
            _offline(logger, null);
        }

        public static void Initializing(this ILogger logger)
        {
            _initializing(logger, null);
        }

        public static void Initialization(this ILogger logger, int attemptCount, int startCount)
        {
            _initialization(logger, attemptCount, startCount, null);
        }

        public static void InStandByMode(this ILogger logger)
        {
            _inStandByMode(logger, null);
        }

        public static void UnhealthyRestart(this ILogger logger)
        {
            _unhealthyRestart(logger, null);
        }

        public static void Stopping(this ILogger logger)
        {
            _stopping(logger, null);
        }

        public static void DidNotShutDown(this ILogger logger)
        {
            _didNotShutDown(logger, null);
        }

        public static void ShutDownCompleted(this ILogger logger)
        {
            _shutDownCompleted(logger, null);
        }

        public static void SkipRestart(this ILogger logger, string state)
        {
            _skipRestart(logger, state, null);
        }

        public static void Restarting(this ILogger logger)
        {
            _restarting(logger, null);
        }

        public static void Restarted(this ILogger logger)
        {
            _restarted(logger, null);
        }

        public static void Building(this ILogger logger, string skipHostStartup, string skipHostJsonConfiguration)
        {
            _building(logger, skipHostStartup, skipHostJsonConfiguration, null);
        }

        public static void StartupWasCanceled(this ILogger logger)
        {
            _startupWasCanceled(logger, null);
        }

        public static void ErrorOccured(this ILogger logger, Exception ex)
        {
            _errorOccured(logger, ex);
        }

        public static void ErrorOccuredInactive(this ILogger logger, Exception ex)
        {
            _errorOccuredInactive(logger, ex);
        }

        public static void CancellationRequested(this ILogger logger)
        {
            _cancellationRequested(logger, null);
        }

        public static void ActiveHostChanging(this ILogger logger, string oldHostInstanceId, string newHostInstanceId)
        {
            _activeHostChanging(logger, oldHostInstanceId, newHostInstanceId, null);
        }

        public static void EnteringRestart(this ILogger logger)
        {
            _enteringRestart(logger, null);
        }
    }
}
