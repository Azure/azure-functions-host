// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.ApplicationInsights.AspNetCore;
using Microsoft.Azure.Documents;
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

        private static readonly Action<ILogger, Guid, Exception> _offline =
            LoggerMessage.Define<Guid>(
                LogLevel.Information,
                new EventId(502, nameof(Offline)),
                "Host created with operation id '{operationId}' is offline.");

        private static readonly Action<ILogger, Guid, Exception> _initializing =
            LoggerMessage.Define<Guid>(
                LogLevel.Information,
                new EventId(503, nameof(Initializing)),
                "Initializing Host. OperationId: '{operationId}'.");

        private static readonly Action<ILogger, int, int, Guid, Exception> _initialization =
            LoggerMessage.Define<int, int, Guid>(
                LogLevel.Information,
                new EventId(504, nameof(Initialization)),
                "Host initialization: ConsecutiveErrors={attemptCount}, StartupCount={startCount}, OperationId={operationId}");

        private static readonly Action<ILogger, Guid, Exception> _inStandByMode =
            LoggerMessage.Define<Guid>(
                LogLevel.Information,
                new EventId(505, nameof(InStandByMode)),
                "Host is in standby mode. OperationId: '{operationId}'.");

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

        private static readonly Action<ILogger, bool, bool, Guid, Exception> _building =
            LoggerMessage.Define<bool, bool, Guid>(
                LogLevel.Information,
                new EventId(513, nameof(Building)),
                "Building host: startup suppressed: '{skipHostStartup}', configuration suppressed: '{skipHostJsonConfiguration}', startup operation id: '{operationId}'");

        private static readonly Action<ILogger, Guid, Exception> _startupOperationWasCanceled =
            LoggerMessage.Define<Guid>(
                LogLevel.Debug,
                new EventId(514, nameof(StartupOperationWasCanceled)),
                "Host startup operation '{operationId}' was canceled.");

        private static readonly Action<ILogger, Guid, Exception> _errorOccuredDuringStartupOperation =
            LoggerMessage.Define<Guid>(
                LogLevel.Error,
                new EventId(515, nameof(ErrorOccuredDuringStartupOperation)),
                "A host error has occurred during startup operation '{operationId}'.");

        private static readonly Action<ILogger, Guid, Exception> _errorOccuredInactive =
            LoggerMessage.Define<Guid>(
                LogLevel.Warning,
                new EventId(516, nameof(ErrorOccuredInactive)),
                "A host error has occurred on an inactive host during startup operation '{operationId}'.");

        private static readonly Action<ILogger, Guid, Exception> _cancellationRequested =
            LoggerMessage.Define<Guid>(
                LogLevel.Debug,
                new EventId(517, nameof(CancellationRequested)),
                "Cancellation requested for startup operation '{operationId}'. A new host will not be started.");

        private static readonly Action<ILogger, string, string, Exception> _activeHostChanging =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug,
                new EventId(518, nameof(ActiveHostChanging)),
                "Active host changing from '{oldHostInstanceId}' to '{newHostInstanceId}'.");

        private static readonly Action<ILogger, Exception> _enteringRestart =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(519, nameof(EnteringRestart)),
                "Restart requested.");

        private static readonly Action<ILogger, Exception> _restartBeforeStart =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(520, nameof(RestartBeforeStart)),
                "RestartAsync was called before StartAsync. Delaying restart until StartAsync has been called.");

        private static readonly Action<ILogger, Guid, Exception> _startupOperationStarting =
            LoggerMessage.Define<Guid>(
                LogLevel.Debug,
                new EventId(521, nameof(StartupOperationStarting)),
                "Startup operation '{operationId}' starting.");

        private static readonly Action<ILogger, Guid, Exception> _cancelingStartupOperationForRestart =
            LoggerMessage.Define<Guid>(
                LogLevel.Debug,
                new EventId(522, nameof(CancelingStartupOperationForRestart)),
                "Canceling startup operation '{operationId}' to unblock restart.");

        // EventId 523 and 524 are defined in ScriptHostStartupOperation.

        private static readonly Action<ILogger, Guid, string, Exception> _startupOperationStartingHost =
            LoggerMessage.Define<Guid, string>(
                LogLevel.Debug,
                new EventId(525, nameof(StartupOperationStartingHost)),
                "Startup operation '{operationId}' is starting host instance '{hostInstanceId}'.");

        private static readonly Action<ILogger, Exception> _scriptHostServiceRestartCanceledByRuntime =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(526, nameof(ScriptHostServiceInitCanceledByRuntime)),
                "Restart cancellation requested by runtime.");

        private static readonly Action<ILogger, string, string, string, string, Exception> _executingHttpRequest =
            LoggerMessage.Define<string, string, string, string>(
                LogLevel.Information,
                new EventId(527, nameof(ExecutingHttpRequest)),
                Properties.Resources.ExecutingHttpRequest);

        private static readonly Action<ILogger, string, string, int, long, Exception> _executedHttpRequest =
            LoggerMessage.Define<string, string, int, long>(
                LogLevel.Information,
                new EventId(528, nameof(ExecutedHttpRequest)),
                Properties.Resources.ExecutedHttpRequest);

        public static void ExecutingHttpRequest(this ILogger logger, string mS_ActivityId, string httpMethod, string userAgent, string uri)
        {
            _executingHttpRequest(logger, mS_ActivityId, httpMethod, userAgent, uri, null);
        }

        public static void ExecutedHttpRequest(this ILogger logger, string mS_ActivityId, string identities, int statusCode, long duration)
        {
            _executedHttpRequest(logger, mS_ActivityId, identities, statusCode, duration, null);
        }

        public static void ScriptHostServiceInitCanceledByRuntime(this ILogger logger)
        {
            _scriptHostServiceInitCanceledByRuntime(logger, null);
        }

        public static void UnhealthyCountExceeded(this ILogger logger, int healthCheckThreshold, TimeSpan healthCheckWindow)
        {
            _unehealthyCountExceeded(logger, healthCheckThreshold, healthCheckWindow, null);
        }

        public static void Offline(this ILogger logger, Guid operationId)
        {
            _offline(logger, operationId, null);
        }

        public static void Initializing(this ILogger logger, Guid operationId)
        {
            _initializing(logger, operationId, null);
        }

        public static void Initialization(this ILogger logger, int attemptCount, int startCount, Guid operationId)
        {
            _initialization(logger, attemptCount, startCount, operationId, null);
        }

        public static void InStandByMode(this ILogger logger, Guid operationId)
        {
            _inStandByMode(logger, operationId, null);
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

        public static void Building(this ILogger logger, bool skipHostStartup, bool skipHostJsonConfiguration, Guid operationId)
        {
            _building(logger, skipHostStartup, skipHostJsonConfiguration, operationId, null);
        }

        public static void StartupOperationWasCanceled(this ILogger logger, Guid operationId)
        {
            _startupOperationWasCanceled(logger, operationId, null);
        }

        public static void ErrorOccuredDuringStartupOperation(this ILogger logger, Guid operationId, Exception ex)
        {
            _errorOccuredDuringStartupOperation(logger, operationId, ex);
        }

        public static void ErrorOccuredInactive(this ILogger logger, Guid operationId, Exception ex)
        {
            _errorOccuredInactive(logger, operationId, ex);
        }

        public static void CancellationRequested(this ILogger logger, Guid operationId)
        {
            _cancellationRequested(logger, operationId, null);
        }

        public static void ActiveHostChanging(this ILogger logger, string oldHostInstanceId, string newHostInstanceId)
        {
            _activeHostChanging(logger, oldHostInstanceId, newHostInstanceId, null);
        }

        public static void EnteringRestart(this ILogger logger)
        {
            _enteringRestart(logger, null);
        }

        public static void RestartBeforeStart(this ILogger logger)
        {
            _restartBeforeStart(logger, null);
        }

        public static void StartupOperationStarting(this ILogger logger, Guid operationId)
        {
            _startupOperationStarting(logger, operationId, null);
        }

        public static void CancelingStartupOperationForRestart(this ILogger logger, Guid operationId)
        {
            _cancelingStartupOperationForRestart(logger, operationId, null);
        }

        public static void StartupOperationStartingHost(this ILogger logger, Guid operationId, string hostInstanceId)
        {
            _startupOperationStartingHost(logger, operationId, hostInstanceId, null);
        }

        public static void ScriptHostServiceRestartCanceledByRuntime(this ILogger logger)
        {
            _scriptHostServiceRestartCanceledByRuntime(logger, null);
        }
    }
}
