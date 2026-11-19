// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// This hosted service is responsible for performing a background SyncTriggers
    /// operation after a successful host startup.
    /// </summary>
    public class FunctionsSyncService : IHostedService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IPrimaryHostStateProvider _primaryHostStateProvider;
        private readonly IFunctionsSyncManager _functionsSyncManager;
        private readonly IEnvironment _environment;
        private readonly object _timerLock = new();
        private Timer _syncTimer;
        private int _eligibilityChecks;
        private volatile bool _stopped;
        private bool _disposed = false;

        public FunctionsSyncService(ILoggerFactory loggerFactory, IScriptHostManager scriptHostManager, IPrimaryHostStateProvider primaryHostStateProvider, IFunctionsSyncManager functionsSyncManager, IEnvironment environment)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);

            DueTime = 30 * 1000;
            _scriptHostManager = scriptHostManager;
            _primaryHostStateProvider = primaryHostStateProvider;
            _functionsSyncManager = functionsSyncManager;
            _environment = environment;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
        }

        // exposed for testing
        internal int DueTime { get; set; }

        internal bool ShouldSyncTriggers
        {
            get
            {
                return _primaryHostStateProvider.IsPrimary &&
                       (_scriptHostManager.State == ScriptHostState.Running) &&
                       !_environment.IsInValidationMode();
            }
        }

        // exposed for testing
        internal int MaxEligibilityChecks { get; set; } = 10;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // create a onetime invocation timer; it is re-armed only while waiting for the host to become eligible
            _syncTimer = new Timer(OnSyncTimerTick, cancellationToken, DueTime, Timeout.Infinite);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            lock (_timerLock)
            {
                _stopped = true;

                // cancel the timer if it has been started
                _syncTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }

            return Task.CompletedTask;
        }

        private async void OnSyncTimerTick(object state)
        {
            try
            {
                var cancellationToken = (CancellationToken)state;

                if (cancellationToken.IsCancellationRequested || _stopped)
                {
                    return;
                }

                if (ShouldSyncTriggers)
                {
                    _logger.LogDebug("Initiating background SyncTriggers operation");
                    await _functionsSyncManager.TrySyncTriggersAsync(isBackgroundSync: true);
                }
                else if (_environment.IsInValidationMode())
                {
                    return;
                }
                else if (++_eligibilityChecks < MaxEligibilityChecks)
                {
                    // The primary host lease may be acquired (or the host may finish starting) after the
                    // initial delay, e.g. following a slot swap or restart. Check again rather than skipping
                    // the startup sync entirely.
                    ScheduleNextCheck();
                }
                else
                {
                    _logger.LogDebug("Skipping background SyncTriggers operation. Host did not become primary and running after {eligibilityChecks} checks.", _eligibilityChecks);
                }
            }
            catch (Exception exc) when (!exc.IsFatal())
            {
                // failures are already logged in the sync triggers call
                // we need to suppress background exceptions from the timer thread
            }
        }

        private void ScheduleNextCheck()
        {
            lock (_timerLock)
            {
                if (!_stopped && !_disposed)
                {
                    _syncTimer?.Change(DueTime, Timeout.Infinite);
                }
            }
        }

        public void Dispose()
        {
            lock (_timerLock)
            {
                if (!_disposed)
                {
                    _syncTimer?.Dispose();
                    _disposed = true;
                }
            }
        }
    }
}
