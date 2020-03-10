// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.AspNetCore;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation.ApplicationId;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebJobsScriptHostService : IHostedService, IScriptHostManager, IServiceProvider, IDisposable
    {
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IScriptWebHostEnvironment _scriptWebHostEnvironment;
        private readonly IScriptHostBuilder _scriptHostBuilder;
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;
        private readonly IMetricsLogger _metricsLogger;
        private readonly HostPerformanceManager _performanceManager;
        private readonly IOptions<HostHealthMonitorOptions> _healthMonitorOptions;
        private readonly SlidingWindow<bool> _healthCheckWindow;
        private readonly Timer _hostHealthCheckTimer;
        private readonly SemaphoreSlim _hostStartSemaphore = new SemaphoreSlim(1, 1);
        private readonly TaskCompletionSource<bool> _hostStartedSource = new TaskCompletionSource<bool>();
        private readonly Task _hostStarted;

        private IHost _host;
        private CancellationTokenSource _startupLoopTokenSource;
        private int _hostStartCount;
        private bool _disposed = false;

        private static IDisposable _telemetryConfiguration;
        private static IDisposable _requestTrackingModule;

        private int _applicationStopping;
        private int _applicationStopped;

        public WebJobsScriptHostService(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IScriptHostBuilder scriptHostBuilder, ILoggerFactory loggerFactory,
            IServiceScopeFactory rootScopeFactory, IScriptWebHostEnvironment scriptWebHostEnvironment, IEnvironment environment,
            HostPerformanceManager hostPerformanceManager, IOptions<HostHealthMonitorOptions> healthMonitorOptions, IMetricsLogger metricsLogger, IApplicationLifetime applicationLifetime)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            // This will no-op if already initialized.
            InitializeApplicationInsightsRequestTracking();

            _applicationLifetime = applicationLifetime;
            RegisterApplicationLifetimeEvents();

            _metricsLogger = metricsLogger;
            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
            _scriptWebHostEnvironment = scriptWebHostEnvironment ?? throw new ArgumentNullException(nameof(scriptWebHostEnvironment));
            _scriptHostBuilder = scriptHostBuilder ?? throw new ArgumentNullException(nameof(scriptHostBuilder));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _performanceManager = hostPerformanceManager ?? throw new ArgumentNullException(nameof(hostPerformanceManager));
            _healthMonitorOptions = healthMonitorOptions ?? throw new ArgumentNullException(nameof(healthMonitorOptions));
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);

            _hostStarted = _hostStartedSource.Task;

            State = ScriptHostState.Default;

            if (ShouldMonitorHostHealth)
            {
                _healthCheckWindow = new SlidingWindow<bool>(_healthMonitorOptions.Value.HealthCheckWindow);
                _hostHealthCheckTimer = new Timer(OnHostHealthCheckTimer, null, TimeSpan.Zero, _healthMonitorOptions.Value.HealthCheckInterval);
            }
        }

        public event EventHandler HostInitializing;

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

        private bool ShutdownRequested => _applicationStopping == 1 || _applicationStopped == 1;

        private IHost ActiveHost
        {
            get
            {
                return _host;
            }

            set
            {
                _logger?.ActiveHostChanging(GetHostInstanceId(_host), GetHostInstanceId(value));
                _host = value;
            }
        }

        public IServiceProvider Services => ActiveHost?.Services;

        public ScriptHostState State { get; private set; }

        public Exception LastError { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the host health monitor should be active.
        /// </summary>
        internal bool ShouldMonitorHostHealth
        {
            get
            {
                return _healthMonitorOptions.Value.Enabled && _environment.IsAppService();
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            CheckFileSystem();
            if (ShutdownRequested)
            {
                return;
            }

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
                    _logger.ScriptHostServiceInitCanceledByRuntime();
                    throw;
                }

                // If the exception was triggered by our loop cancellation token, just ignore as
                // it doesn't indicate an issue.
            }
        }

        private void CheckFileSystem()
        {
            // Shutdown if RunFromZipFailed
            if (_environment.IsZipDeployment(validate: false))
            {
                string path = Path.Combine(_applicationHostOptions.CurrentValue.ScriptPath, ScriptConstants.RunFromPackageFailedFileName);
                if (File.Exists(path))
                {
                    _logger.LogError($"Shutting down host due to presence of {path}");
                    _applicationLifetime.StopApplication();
                }
            }
        }

        private async Task StartHostAsync(CancellationToken cancellationToken, int attemptCount = 0,
            JobHostStartupMode startupMode = JobHostStartupMode.Normal, Guid? parentOperationId = null)
        {
            // Add this to the list of trackable startup operations. Restarts can use this to cancel any ongoing or pending operations.
            var activeOperation = ScriptHostStartupOperation.Create(cancellationToken, _logger, parentOperationId);

            using (_metricsLogger.LatencyEvent(MetricEventNames.ScriptHostManagerStartService))
            {
                try
                {
                    await _hostStartSemaphore.WaitAsync();

                    // Now that we're inside the semaphore, set this task as completed. This prevents
                    // restarts from being invoked (via the PlaceholderSpecializationMiddleware) before
                    // the IHostedService has ever started.
                    _hostStartedSource.TrySetResult(true);

                    await UnsynchronizedStartHostAsync(activeOperation, attemptCount, startupMode);
                }
                finally
                {
                    activeOperation.Dispose();
                    _hostStartSemaphore.Release();
                }
            }
        }

        /// <summary>
        /// Starts the host without taking a lock. Callers must take a lock on _hostStartSemaphore
        /// before calling this method. Host starts and restarts must be synchronous to prevent orphaned
        /// hosts or an incorrect ActiveHost.
        /// </summary>
        private async Task UnsynchronizedStartHostAsync(ScriptHostStartupOperation activeOperation, int attemptCount = 0, JobHostStartupMode startupMode = JobHostStartupMode.Normal)
        {
            IHost localHost = null;
            var currentCancellationToken = activeOperation.CancellationTokenSource.Token;
            _logger.StartupOperationStarting(activeOperation.Id);

            try
            {
                currentCancellationToken.ThrowIfCancellationRequested();

                // if we were in an error state retain that,
                // otherwise move to default
                if (State != ScriptHostState.Error)
                {
                    State = ScriptHostState.Default;
                }

                bool isOffline = Utility.CheckAppOffline(_environment, _applicationHostOptions.CurrentValue.ScriptPath);
                State = isOffline ? ScriptHostState.Offline : State;
                bool hasNonTransientErrors = startupMode.HasFlag(JobHostStartupMode.HandlingNonTransientError);

                // If we're in a non-transient error state or offline, skip host initialization
                bool skipJobHostStartup = isOffline || hasNonTransientErrors;
                bool skipHostJsonConfiguration = startupMode == JobHostStartupMode.HandlingConfigurationParsingError;
                _logger.Building(skipJobHostStartup, skipHostJsonConfiguration, activeOperation.Id);

                using (_metricsLogger.LatencyEvent(MetricEventNames.ScriptHostManagerBuildScriptHost))
                {
                    localHost = BuildHost(skipJobHostStartup, skipHostJsonConfiguration);
                }

                ActiveHost = localHost;

                var scriptHost = (ScriptHost)ActiveHost.Services.GetService<ScriptHost>();
                if (scriptHost != null)
                {
                    scriptHost.HostInitializing += OnHostInitializing;
                    scriptHost.HostInitialized += OnHostInitialized;
                }

                LogInitialization(localHost, isOffline, attemptCount, ++_hostStartCount, activeOperation.Id);

                if (!_scriptWebHostEnvironment.InStandbyMode)
                {
                    // At this point we know that App Insights is initialized (if being used), so we
                    // can dispose this early request tracking module, which forces our new one to take over.
                    DisposeRequestTrackingModule();
                }

                currentCancellationToken.ThrowIfCancellationRequested();

                var hostInstanceId = GetHostInstanceId(localHost);
                _logger.StartupOperationStartingHost(activeOperation.Id, hostInstanceId);

                using (_metricsLogger.LatencyEvent(MetricEventNames.ScriptHostManagerStartScriptHost))
                {
                    await localHost.StartAsync(currentCancellationToken);
                }

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
                GetHostLogger(localHost).StartupOperationWasCanceled(activeOperation.Id);
                throw;
            }
            catch (Exception exc)
            {
                bool isActiveHost = ReferenceEquals(localHost, ActiveHost);
                ILogger logger = GetHostLogger(localHost);

                if (isActiveHost)
                {
                    LastError = exc;
                    State = ScriptHostState.Error;
                    logger.ErrorOccuredDuringStartupOperation(activeOperation.Id, exc);
                }
                else
                {
                    // Another host has been created before this host
                    // threw its startup exception. We want to make sure it
                    // doesn't control the state of the service.
                    logger.ErrorOccuredInactive(activeOperation.Id, exc);
                }

                attemptCount++;

                if (ShutdownHostIfUnhealthy())
                {
                    return;
                }

                if (isActiveHost)
                {
                    // We don't want to return disposed services via the Services property, so
                    // set this to null before calling Orphan().
                    ActiveHost = null;
                }

                var orphanTask = Orphan(localHost)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            t.Exception.Handle(e => true);
                        }
                    }, TaskContinuationOptions.ExecuteSynchronously);

                // Use the fallback logger now, as we cannot trust when the host
                // logger will be disposed.
                logger = _logger;

                if (currentCancellationToken.IsCancellationRequested)
                {
                    logger.CancellationRequested(activeOperation.Id);
                    currentCancellationToken.ThrowIfCancellationRequested();
                }

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
                    logger.LogDebug($"Starting new host with '{nextStartupAttemptMode}' and parent operation id '{activeOperation.Id}'.");
                    Task ignore = StartHostAsync(currentCancellationToken, attemptCount, nextStartupAttemptMode, activeOperation.Id);
                }
                else
                {
                    logger.LogDebug($"Will start a new host after delay.");

                    await Utility.DelayWithBackoffAsync(attemptCount, currentCancellationToken, min: TimeSpan.FromSeconds(1), max: TimeSpan.FromMinutes(2), logger: logger);

                    if (currentCancellationToken.IsCancellationRequested)
                    {
                        logger.LogDebug($"Cancellation for operation '{activeOperation.Id}' requested during delay. A new host will not be started.");
                        currentCancellationToken.ThrowIfCancellationRequested();
                    }

                    logger.LogDebug("Starting new host after delay.");
                    Task ignore = StartHostAsync(currentCancellationToken, attemptCount, parentOperationId: activeOperation.Id);
                }
            }
        }

        private void DisposeRequestTrackingModule()
        {
            _requestTrackingModule?.Dispose();
            _requestTrackingModule = null;

            _telemetryConfiguration?.Dispose();
            _telemetryConfiguration = null;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _startupLoopTokenSource?.Cancel();

            State = ScriptHostState.Stopping;
            _logger.Stopping();

            var currentHost = ActiveHost;
            ActiveHost = null;
            Task stopTask = Orphan(currentHost, cancellationToken);
            Task result = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));

            if (result != stopTask)
            {
                _logger.DidNotShutDown();
            }
            else
            {
                _logger.ShutDownCompleted();
            }

            State = ScriptHostState.Stopped;
        }

        public async Task RestartHostAsync(CancellationToken cancellationToken)
        {
            if (ShutdownRequested)
            {
                return;
            }

            using (_metricsLogger.LatencyEvent(MetricEventNames.ScriptHostManagerRestartService))
            {
                // Do not invoke a restart if the host has not yet been started. This can lead
                // to invalid state.
                if (!_hostStarted.IsCompleted)
                {
                    _logger.RestartBeforeStart();
                    await _hostStarted;
                }

                _logger.EnteringRestart();

                // If anything is mid-startup, cancel it.
                _startupLoopTokenSource?.Cancel();
                foreach (var startupOperation in ScriptHostStartupOperation.ActiveOperations)
                {
                    _logger.CancelingStartupOperationForRestart(startupOperation.Id);
                    try
                    {
                        startupOperation.CancellationTokenSource.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // This can be disposed at any time.
                    }
                }

                try
                {
                    await _hostStartSemaphore.WaitAsync();

                    if (State == ScriptHostState.Stopping || State == ScriptHostState.Stopped)
                    {
                        _logger.SkipRestart(State.ToString());
                        return;
                    }

                    State = ScriptHostState.Default;
                    _logger.Restarting();

                    var previousHost = ActiveHost;
                    ActiveHost = null;

                    using (var activeOperation = ScriptHostStartupOperation.Create(cancellationToken, _logger))
                    {
                        Task startTask = UnsynchronizedStartHostAsync(activeOperation);
                        Task stopTask = Orphan(previousHost, cancellationToken);

                        await startTask;
                    }

                    _logger.Restarted();
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.ScriptHostServiceRestartCanceledByRuntime();
                        throw;
                    }

                    // If the exception was triggered by our startup operation cancellation token, just ignore as
                    // it doesn't indicate an issue.
                }
                finally
                {
                    _hostStartSemaphore.Release();
                }
            }
        }

        private void OnHostInitializing(object sender, EventArgs e)
        {
            // we check host health before starting to avoid starting
            // the host when connection or other issues exist
            IsHostHealthy(throwWhenUnhealthy: true);

            // We invoke any registered event delegates during Host Initialization
            HostInitializing?.Invoke(sender, e);
        }

        /// <summary>
        /// Called after the host has been fully initialized, but before it
        /// has been started.
        /// </summary>
        private void OnHostInitialized(object sender, EventArgs e)
        {
            State = ScriptHostState.Initialized;
        }

        private IHost BuildHost(bool skipHostStartup, bool skipHostJsonConfiguration)
        {
            return _scriptHostBuilder.BuildHost(skipHostStartup, skipHostJsonConfiguration);
        }

        private string GetHostInstanceId(IHost host)
        {
            IOptions<ScriptJobHostOptions> scriptHostOptions = null;

            try
            {
                scriptHostOptions = host?.Services.GetService<IOptions<ScriptJobHostOptions>>();
            }
            catch (ObjectDisposedException)
            {
                // If the host is disposed, we cannot access services.
            }

            return scriptHostOptions?.Value?.InstanceId;
        }

        private ILogger GetHostLogger(IHost host)
        {
            ILoggerFactory hostLoggerFactory = null;
            try
            {
                hostLoggerFactory = host?.Services?.GetService<ILoggerFactory>();
            }
            catch (ObjectDisposedException)
            {
                // If the host is disposed, we cannot access services.
            }

            // Attempt to get the host logger with JobHost configuration applied
            // using the default logger as a fallback
            return hostLoggerFactory?.CreateLogger(LogCategories.Startup) ?? _logger;
        }

        private void LogInitialization(IHost host, bool isOffline, int attemptCount, int startCount, Guid operationId)
        {
            var logger = GetHostLogger(host);

            if (isOffline)
            {
                logger.Offline(operationId);
            }
            else
            {
                logger.Initializing(operationId);
            }
            logger.Initialization(attemptCount, startCount, operationId);

            if (_scriptWebHostEnvironment.InStandbyMode)
            {
                // Reading the string from resources to make sure resource loading code path is warmed up during placeholder as well.
                logger.InStandByMode(operationId);
            }
        }

        private void OnHostHealthCheckTimer(object state)
        {
            bool isHealthy = IsHostHealthy();
            _healthCheckWindow.AddEvent(isHealthy);

            if (!isHealthy && State == ScriptHostState.Running)
            {
                // This periodic check allows us to break out of the host run
                // loop. The health check performed in OnHostInitializing will then
                // fail and we'll enter a restart loop (exponentially backing off)
                // until the host is healthy again and we can resume host processing.
                _logger.UnhealthyRestart();
                var tIgnore = RestartHostAsync(CancellationToken.None);
            }
        }

        internal bool IsHostHealthy(bool throwWhenUnhealthy = false)
        {
            if (!ShouldMonitorHostHealth)
            {
                return true;
            }

            var exceededCounters = new Collection<string>();
            if (_performanceManager.IsUnderHighLoad(exceededCounters))
            {
                string formattedCounters = string.Join(", ", exceededCounters);
                if (throwWhenUnhealthy)
                {
                    throw new InvalidOperationException($"Host thresholds exceeded: [{formattedCounters}]. For more information, see https://aka.ms/functions-thresholds.");
                }
                return false;
            }

            return true;
        }

        private bool ShutdownHostIfUnhealthy()
        {
            if (ShouldMonitorHostHealth && _healthCheckWindow.GetEvents().Count(isHealthy => !isHealthy) > _healthMonitorOptions.Value.HealthCheckThreshold)
            {
                // if the number of times the host has been unhealthy in
                // the current time window exceeds the threshold, recover by
                // initiating shutdown
                _logger.UnhealthyCountExceeded(_healthMonitorOptions.Value.HealthCheckThreshold, _healthMonitorOptions.Value.HealthCheckWindow);
                _applicationLifetime.StopApplication();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Remove the <see cref="IHost"/> instance from the live instances collection,
        /// allowing it to finish currently executing functions before stopping and disposing of it.
        /// </summary>
        /// <param name="instance">The <see cref="IHost"/> instance to remove</param>
        private async Task Orphan(IHost instance, CancellationToken cancellationToken = default)
        {
            try
            {
                var scriptHost = (ScriptHost)instance?.Services.GetService<ScriptHost>();
                if (scriptHost != null)
                {
                    scriptHost.HostInitializing -= OnHostInitializing;
                    scriptHost.HostInitialized -= OnHostInitialized;
                }
            }
            catch (ObjectDisposedException)
            {
                // If the instance is already disposed, we cannot access its services.
            }

            try
            {
                await (instance?.StopAsync(cancellationToken) ?? Task.CompletedTask);
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // some errors are expected here - e.g. in error/shutdown situations
                // we might attempt to stop the host before it has fully started
            }
            finally
            {
                if (instance != null)
                {
                    GetHostLogger(instance).LogDebug("Disposing ScriptHost.");
                    instance.Dispose();
                }
            }
        }

        private void RegisterApplicationLifetimeEvents()
        {
            _applicationLifetime.ApplicationStopping.Register(() =>
            {
                Interlocked.Exchange(ref _applicationStopping, 1);
            });

            _applicationLifetime.ApplicationStopped.Register(() =>
            {
                Interlocked.Exchange(ref _applicationStopped, 1);
            });
        }

        private static void InitializeApplicationInsightsRequestTracking()
        {
            if (_requestTrackingModule != null)
            {
                return;
            }

            // Requests may come in before the JobHost has started (like during cold starts), which means
            // they will not be properly tracked by Application Insights because there's nothing listening
            // for them yet. This wires up the request tracking module with default values to catch those
            // events and properly create an Activity. Once the JobHost has started, we dispose this and the
            // JobHost tracking module takes over.
            var module = new RequestTrackingTelemetryModule(new ApplicationInsightsApplicationIdProvider())
            {
                CollectionOptions = new RequestCollectionOptions
                {
                    TrackExceptions = false,
                    EnableW3CDistributedTracing = true,
                    InjectResponseHeaders = true
                }
            };

            var telemetryConfig = new TelemetryConfiguration();

            module.Initialize(telemetryConfig);

            _telemetryConfiguration = telemetryConfig;
            _requestTrackingModule = module;
        }

        public object GetService(Type serviceType)
        {
            return Services?.GetService(serviceType);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _startupLoopTokenSource?.Dispose();
                    _hostStartSemaphore.Dispose();
                    DisposeRequestTrackingModule();
                }
                _disposed = true;
            }
        }

        public void Dispose() => Dispose(true);
    }
}
