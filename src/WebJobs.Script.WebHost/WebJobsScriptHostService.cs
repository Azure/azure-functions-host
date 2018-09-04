// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebJobsScriptHostService : IHostedService, IScriptHostManager, IDisposable
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IScriptHostBuilder _scriptHostBuilder;
        private readonly ILogger _logger;
        private IHost _host;
        private CancellationTokenSource _startupLoopTokenSource;
        private int _hostStartCount;
        private bool _disposed = false;

        public WebJobsScriptHostService(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IScriptHostBuilder scriptHostBuilder, ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
            _scriptHostBuilder = scriptHostBuilder ?? throw new ArgumentNullException(nameof(scriptHostBuilder));

            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);

            State = ScriptHostState.Default;
        }

        [Flags]
        private enum JobHostStartupMode
        {
            Normal = 0,
            Offline = 1,
            HandlingError = 2,
            HandlingNonTransientError = 4 | HandlingError,
            HandlingConfigurationParsingError = 8 | HandlingError,
            HandlingInitializationError = 16 | HandlingNonTransientError
        }

        public IServiceProvider Services => _host?.Services;

        public ScriptHostState State { get; private set; }

        public Exception LastError { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _startupLoopTokenSource = new CancellationTokenSource();
            var startupLoopToken = _startupLoopTokenSource.Token;
            var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(startupLoopToken, cancellationToken);

            try
            {
                await StartHostAsync(tokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Initialization cancellation requested by runtime.");
                    throw;
                }

                // If the exception was triggered by our loop cancellation token, just ignore as
                // it doesn't indicate an issue.
            }
        }

        private async Task StartHostAsync(CancellationToken cancellationToken, int attemptCount = 0, JobHostStartupMode startupMode = JobHostStartupMode.Normal)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                bool isOffline = CheckAppOffline();
                bool hasNonTransientErrors = startupMode.HasFlag(JobHostStartupMode.HandlingNonTransientError);

                // If we're in a non-transient error state or offline, skip host initialization
                bool skipJobHostStartup = isOffline || hasNonTransientErrors;

                _host = BuildHost(skipJobHostStartup, skipHostJsonConfiguration: startupMode == JobHostStartupMode.HandlingConfigurationParsingError);

                LogInitialization(isOffline, attemptCount, ++_hostStartCount);

                await _host.StartAsync(cancellationToken);

                if (!startupMode.HasFlag(JobHostStartupMode.HandlingError))
                {
                    LastError = null;

                    if (!isOffline)
                    {
                        State = ScriptHostState.Running;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exc)
            {
                LastError = exc;
                State = ScriptHostState.Error;
                attemptCount++;

                ILogger logger = GetHostLogger();

                logger.LogError(exc, "A ScriptHost error has occurred");

                var orphanTask = Orphan(_host, logger)
                    .ContinueWith(t =>
                           {
                               if (t.IsFaulted)
                               {
                                   t.Exception.Handle(e => true);
                               }
                           }, TaskContinuationOptions.ExecuteSynchronously);

                cancellationToken.ThrowIfCancellationRequested();

                var nextStartupAttemptMode = JobHostStartupMode.Normal;

                if (exc is HostConfigurationException)
                {
                    // Try starting the host without parsing host.json. This will start up a
                    // minimal host and allow the portal to see the error. Any modification will restart again.
                    nextStartupAttemptMode = JobHostStartupMode.HandlingConfigurationParsingError;
                }
                else if (exc is HostInitializationException)
                {
                    nextStartupAttemptMode = JobHostStartupMode.HandlingInitializationError;
                }

                if (nextStartupAttemptMode != JobHostStartupMode.Normal)
                {
                    Task ignore = StartHostAsync(cancellationToken, attemptCount, nextStartupAttemptMode);
                }
                else
                {
                    await Utility.DelayWithBackoffAsync(attemptCount, cancellationToken, min: TimeSpan.FromSeconds(1), max: TimeSpan.FromMinutes(2))
                        .ContinueWith(t =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return StartHostAsync(cancellationToken, attemptCount);
                        });
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _startupLoopTokenSource?.Cancel();

            State = ScriptHostState.Stopping;
            _logger.LogInformation("Stopping host...");

            var currentHost = _host;
            Task stopTask = Orphan(currentHost, _logger, cancellationToken);
            Task result = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(10)));

            if (result != stopTask)
            {
                _logger.LogWarning("Host did not shutdown within its allotted time.");
            }
            else
            {
                _logger.LogInformation("Host shutdown completed.");
            }

            State = ScriptHostState.Stopped;
        }

        public async Task RestartHostAsync(CancellationToken cancellationToken)
        {
            if (State == ScriptHostState.Stopping || State == ScriptHostState.Stopped)
            {
                return;
            }

            _startupLoopTokenSource?.Cancel();

            State = ScriptHostState.Default;

            _logger.LogInformation("Restarting script host.");

            var previousHost = _host;
            Task startTask = StartAsync(cancellationToken);
            Task stopTask = Orphan(previousHost, _logger, cancellationToken);

            await startTask;

            _logger.LogInformation("Script host restarted.");
        }

        private IHost BuildHost(bool skipHostStartup = false, bool skipHostJsonConfiguration = false)
        {
            return _scriptHostBuilder.BuildHost(skipHostStartup, skipHostJsonConfiguration);
        }

        private ILogger GetHostLogger()
        {
            var hostLoggerFactory = _host?.Services.GetService<ILoggerFactory>();

            // Attempt to get the host loger with JobHost configuration applied
            // using the default logger as a fallback
            return hostLoggerFactory?.CreateLogger(LogCategories.Startup) ?? _logger;
        }

        private void LogInitialization(bool isOffline, int attemptCount, int startCount)
        {
            var logger = GetHostLogger();

            var log = isOffline ? "Host is offline." : "Initializing Host.";
            logger.LogInformation(log);
            logger.LogInformation($"Host initialization: ConsecutiveErrors={attemptCount}, StartupCount={startCount}");

            if (_scriptWebHostEnvironment.InStandbyMode)
            {
                logger.LogInformation("Host is in standby mode");
            }
        }

        private bool CheckAppOffline()
        {
            // check if we should be in an offline state
            string offlineFilePath = Path.Combine(_applicationHostOptions.CurrentValue.ScriptPath, ScriptConstants.AppOfflineFileName);
            if (File.Exists(offlineFilePath))
            {
                State = ScriptHostState.Offline;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Remove the <see cref="IHost"/> instance from the live instances collection,
        /// allowing it to finish currently executing functions before stopping and disposing of it.
        /// </summary>
        /// <param name="instance">The <see cref="IHost"/> instance to remove</param>
        private async Task Orphan(IHost instance, ILogger logger, CancellationToken cancellationToken = default)
        {
            try
            {
                await (instance?.StopAsync(cancellationToken) ?? Task.CompletedTask);
            }
            catch (Exception)
            {
                //  logger.LogTrace(exc, "Error stopping and disposing of host");
            }
            finally
            {
                instance?.Dispose();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _startupLoopTokenSource?.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose() => Dispose(true);
    }
}
