// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Class encapsulating a <see cref="ScriptHost"/> an keeping a singleton
    /// instance always alive, restarting as necessary.
    /// </summary>
    public class ScriptHostManager : IScriptHostEnvironment, IDisposable
    {
        public const int HostCheckTimeoutSeconds = 30;
        public const int HostCheckPollingIntervalMilliseconds = 500;

        private readonly ScriptHostConfiguration _config;
        private readonly IScriptHostFactory _scriptHostFactory;
        private readonly ILoggerFactoryBuilder _loggerFactoryBuilder;
        private readonly IScriptHostEnvironment _environment;
        private readonly IDisposable _fileEventSubscription;
        private readonly StructuredLogWriter _structuredLogWriter;
        private readonly HostPerformanceManager _performanceManager;
        private readonly Timer _hostHealthCheckTimer;
        private readonly TimeSpan hostHealthCheckInterval = TimeSpan.FromSeconds(15);
        private ScriptHost _currentInstance;
        private int _hostStartCount;

        // ScriptHosts are not thread safe, so be clear that only 1 thread at a time operates on each instance.
        // List of all outstanding ScriptHost instances. Only 1 of these (specified by _currentInstance)
        // should be listening at a time. The others are "orphaned" and exist to finish executing any functions
        // and will then remove themselves from this list.
        private HashSet<ScriptHost> _liveInstances = new HashSet<ScriptHost>();

        private bool _disposed;
        private bool _stopped;
        private AutoResetEvent _stopEvent = new AutoResetEvent(false);
        private AutoResetEvent _restartHostEvent = new AutoResetEvent(false);
        private TraceWriter _traceWriter;
        private ILogger _logger;

        private ScriptSettingsManager _settingsManager;
        private CancellationTokenSource _restartDelayTokenSource;

        public ScriptHostManager(
            ScriptHostConfiguration config,
            IScriptEventManager eventManager = null,
            IScriptHostEnvironment environment = null,
            ILoggerFactoryBuilder loggerFactoryBuilder = null,
            HostPerformanceManager hostPerformanceManager = null)
            : this(config, ScriptSettingsManager.Instance, new ScriptHostFactory(), eventManager, environment, loggerFactoryBuilder, hostPerformanceManager)
        {
            if (config.FileWatchingEnabled)
            {
                // We only setup a subscription here as the actual ScriptHost will create the publisher
                // when initialized.
                _fileEventSubscription = EventManager.OfType<FileEvent>()
                     .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal))
                     .Subscribe(e => OnScriptFileChanged(null, e.FileChangeArguments));
            }
        }

        public ScriptHostManager(ScriptHostConfiguration config,
            ScriptSettingsManager settingsManager,
            IScriptHostFactory scriptHostFactory,
            IScriptEventManager eventManager = null,
            IScriptHostEnvironment environment = null,
            ILoggerFactoryBuilder loggerFactoryBuilder = null,
            HostPerformanceManager hostPerformanceManager = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (settingsManager == null)
            {
                throw new ArgumentNullException(nameof(settingsManager));
            }

            scriptHostFactory = scriptHostFactory ?? new ScriptHostFactory();
            _environment = environment ?? this;
            _config = config;
            _settingsManager = settingsManager;
            _scriptHostFactory = scriptHostFactory;
            _loggerFactoryBuilder = loggerFactoryBuilder;

            EventManager = eventManager ?? new ScriptEventManager();

            _structuredLogWriter = new StructuredLogWriter(EventManager, config.RootLogPath);
            _performanceManager = hostPerformanceManager ?? new HostPerformanceManager(settingsManager);

            // TEMP : temporarily disabling this until the feature is improved
            bool periodicHealthCheckEnabled = false;
            if (periodicHealthCheckEnabled && config.HostHealthMonitorEnabled && settingsManager.IsAzureEnvironment)
            {
                _hostHealthCheckTimer = new Timer(OnHostHealthCheckTimer, null, TimeSpan.Zero, hostHealthCheckInterval);
            }
        }

        protected HostPerformanceManager PerformanceManager => _performanceManager;

        protected IScriptEventManager EventManager { get; }

        public virtual ScriptHost Instance
        {
            get
            {
                return _currentInstance;
            }
        }

        /// <summary>
        /// Gets the current state of the host.
        /// </summary>
        public virtual ScriptHostState State { get; private set; }

        /// <summary>
        /// Gets the last host <see cref="Exception"/> that has occurred.
        /// </summary>
        public virtual Exception LastError { get; private set; }

        /// <summary>
        /// Returns a value indicating whether the host can accept function invoke requests.
        /// </summary>
        /// <remarks>
        /// The host doesn't have to be fully started for it to allow direct function invocations
        /// to be processed.
        /// </remarks>
        /// <returns>True if the host can accept invoke requests, false otherwise.</returns>
        public bool CanInvoke()
        {
            return State == ScriptHostState.Initialized || State == ScriptHostState.Running;
        }

        public void RunAndBlock(CancellationToken cancellationToken = default(CancellationToken))
        {
            int consecutiveErrorCount = 0;
            do
            {
                ScriptHost newInstance = null;

                try
                {
                    // if we were in an error state retain that,
                    // otherwise move to default
                    if (State != ScriptHostState.Error)
                    {
                        State = ScriptHostState.Default;
                    }

                    OnCreatingHost();

                    // Create a new host config, but keep the host id from existing one
                    _config.HostConfig = new JobHostConfiguration(_settingsManager.Configuration)
                    {
                        HostId = _config.HostConfig.HostId
                    };
                    OnInitializeConfig(_config);
                    newInstance = _scriptHostFactory.Create(_environment, EventManager, _settingsManager, _config, _loggerFactoryBuilder);
                    newInstance.HostInitialized += OnHostInitialized;
                    newInstance.HostStarted += OnHostStarted;
                    _traceWriter = newInstance.TraceWriter;
                    _logger = newInstance.Logger;

                    _currentInstance = newInstance;
                    lock (_liveInstances)
                    {
                        _liveInstances.Add(newInstance);
                        _hostStartCount++;
                    }

                    string extensionVersion = _settingsManager.GetSetting(EnvironmentSettingNames.FunctionsExtensionVersion);
                    string hostId = newInstance.ScriptConfig.HostConfig.HostId;
                    string message = $"Starting Host (HostId={hostId}, Version={ScriptHost.Version}, ProcessId={Process.GetCurrentProcess().Id}, Debug={newInstance.InDebugMode}, ConsecutiveErrors={consecutiveErrorCount}, StartupCount={_hostStartCount}, FunctionsExtensionVersion={extensionVersion})";
                    _traceWriter?.Info(message);
                    _logger?.LogInformation(message);

                    newInstance.StartAsync(cancellationToken).GetAwaiter().GetResult();

                    // log any function initialization errors
                    LogErrors(newInstance);

                    LastError = null;
                    consecutiveErrorCount = 0;
                    _restartDelayTokenSource = null;

                    // Wait for a restart signal. This event will automatically reset.
                    // While we're restarting, it is possible for another restart to be
                    // signaled. That is fine - the restart will be processed immediately
                    // once we get to this line again. The important thing is that these
                    // restarts are only happening on a single thread.
                    WaitHandle.WaitAny(new WaitHandle[]
                    {
                        cancellationToken.WaitHandle,
                        _restartHostEvent,
                        _stopEvent
                    });

                    // Orphan the current host instance. We're stopping it, so it won't listen for any new functions
                    // it will finish any currently executing functions and then clean itself up.
                    // Spin around and create a new host instance.
                    Task.Run(() => Orphan(newInstance)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                t.Exception.Handle(e => true);
                            }
                        }, TaskContinuationOptions.ExecuteSynchronously));
                }
                catch (Exception ex)
                {
                    State = ScriptHostState.Error;
                    LastError = ex;
                    consecutiveErrorCount++;

                    // We need to keep the host running, so we catch and log any errors
                    // then restart the host
                    string message = "A ScriptHost error has occurred";
                    _traceWriter?.Error(message, ex);
                    _logger?.LogError(0, ex, message);

                    // If a ScriptHost instance was created before the exception was thrown
                    // Orphan and cleanup that instance.
                    if (newInstance != null)
                    {
                        Task.Run(() => Orphan(newInstance, forceStop: true)
                            .ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                {
                                    t.Exception.Handle(e => true);
                                }
                            }, TaskContinuationOptions.ExecuteSynchronously));
                    }

                    // attempt restarts using an exponential backoff strategy
                    CreateRestartBackoffDelay(consecutiveErrorCount).GetAwaiter().GetResult();
                }
            }
            while (!_stopped && !cancellationToken.IsCancellationRequested);
        }

        private void OnHostInitialized(object sender, EventArgs e)
        {
            OnHostInitialized();
        }

        private void OnHostStarted(object sender, EventArgs e)
        {
            OnHostStarted();
        }

        private Task CreateRestartBackoffDelay(int consecutiveErrorCount)
        {
            _restartDelayTokenSource = new CancellationTokenSource();

            return Utility.DelayWithBackoffAsync(consecutiveErrorCount, _restartDelayTokenSource.Token,
                min: TimeSpan.FromSeconds(1),
                max: TimeSpan.FromMinutes(2));
        }

        private static void LogErrors(ScriptHost host)
        {
            if (host.FunctionErrors.Count > 0)
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "The following {0} functions are in error:", host.FunctionErrors.Count));
                foreach (var error in host.FunctionErrors)
                {
                    string functionErrors = string.Join(Environment.NewLine, error.Value);
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}: {1}", error.Key, functionErrors));
                }
                string message = builder.ToString();
                host.TraceWriter.Error(message);
                host.Logger?.LogError(message);
            }
        }

        /// <summary>
        /// Remove the <see cref="ScriptHost"/> instance from the live instances collection,
        /// allowing it to finish currently executing functions before stopping and disposing of it.
        /// </summary>
        /// <param name="instance">The <see cref="ScriptHost"/> instance to remove</param>
        /// <param name="forceStop">Forces the call to stop and dispose of the instance, even if it isn't present in the live instances collection.</param>
        private async Task Orphan(ScriptHost instance, bool forceStop = false)
        {
            instance.HostInitialized -= OnHostInitialized;
            instance.HostStarted -= OnHostStarted;

            lock (_liveInstances)
            {
                bool removed = _liveInstances.Remove(instance);
                if (!forceStop && !removed)
                {
                    return; // somebody else is handling it
                }
            }

            try
            {
                // this thread now owns the instance
                string message = "Stopping Host";
                instance.TraceWriter?.Info(message);
                instance.Logger?.LogInformation(message);

                await instance.StopAsync();
            }
            finally
            {
                instance.Dispose();
            }
        }

        /// <summary>
        /// Handler for any file changes under the root script path
        /// </summary>
        private void OnScriptFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_restartDelayTokenSource != null && !_restartDelayTokenSource.IsCancellationRequested)
            {
                // if we're currently awaiting an error/restart delay
                // cancel that
                _restartDelayTokenSource.Cancel();
            }
        }

        public async Task StopAsync()
        {
            _stopped = true;

            try
            {
                _stopEvent.Set();
                ScriptHost[] instances = GetLiveInstancesAndClear();

                Task[] tasksStop = Array.ConvertAll(instances, p => StopAndDisposeAsync(p));
                await Task.WhenAll(tasksStop);

                State = ScriptHostState.Default;
            }
            catch
            {
                // best effort
            }
        }

        public void Stop()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        private static async Task StopAndDisposeAsync(ScriptHost instance)
        {
            try
            {
                await instance.StopAsync();
            }
            catch
            {
                // best effort
            }
            finally
            {
                instance.Dispose();
            }
        }

        private ScriptHost[] GetLiveInstancesAndClear()
        {
            ScriptHost[] instances;
            lock (_liveInstances)
            {
                instances = _liveInstances.ToArray();
                _liveInstances.Clear();
            }

            return instances;
        }

        protected virtual void OnInitializeConfig(ScriptHostConfiguration config)
        {
            var loggingConnectionString = config.HostConfig.DashboardConnectionString;
            if (string.IsNullOrEmpty(loggingConnectionString))
            {
                // if no Dashboard connection string is provided, set this to null
                // to prevent host startup failure
                config.HostConfig.DashboardConnectionString = null;
            }
        }

        protected virtual void OnHostInitialized()
        {
            State = ScriptHostState.Initialized;
        }

        /// <summary>
        /// Called after the host has been started.
        /// </summary>
        protected virtual void OnHostStarted()
        {
            var metricsLogger = _config.HostConfig.GetService<IMetricsLogger>();
            metricsLogger.LogEvent(new HostStarted(Instance));

            State = ScriptHostState.Running;
        }

        /// <summary>
        /// Called immediately before we begin creating the host.
        /// </summary>
        protected virtual void OnCreatingHost()
        {
            IsHostHealthy(throwWhenUnhealthy: true);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                ScriptHost[] instances = GetLiveInstancesAndClear();
                foreach (var instance in instances)
                {
                    try
                    {
                        instance.Dispose();
                    }
                    catch (Exception exc) when (!exc.IsFatal())
                    {
                        // Best effort
                    }
                }

                _hostHealthCheckTimer?.Dispose();
                _stopEvent.Dispose();
                _restartDelayTokenSource?.Dispose();
                _fileEventSubscription?.Dispose();
                _structuredLogWriter.Dispose();
                _restartHostEvent.Dispose();

                _disposed = true;
            }
        }

        public virtual void RestartHost()
        {
            // We reset the state immediately here to ensure that
            // any external calls to CanInvoke will return false
            // immediately after the restart has been initiated.
            // Note: this state change must be done BEFORE we set
            // the event below to avoid a race condition with the
            // main host lifetime loop.
            State = ScriptHostState.Default;

            _restartHostEvent.Set();
        }

        public virtual void Shutdown()
        {
            Stop();

            Process.GetCurrentProcess().Close();
        }

        private void OnHostHealthCheckTimer(object state)
        {
            if (State == ScriptHostState.Running && !IsHostHealthy())
            {
                // This periodic check allows us to break out of the host run
                // loop. The health check performed in OnHostStarting will then
                // fail and we'll enter a restart loop (exponentially backing off)
                // until the host is healthy again and we can resume host processing.
                RestartHost();
            }
        }

        internal bool IsHostHealthy(bool throwWhenUnhealthy = false)
        {
            if (!_config.HostHealthMonitorEnabled || !_settingsManager.IsAzureEnvironment)
            {
                return true;
            }

            var exceededCounters = new Collection<string>();
            if (PerformanceManager.IsUnderHighLoad(exceededCounters))
            {
                string formattedCounters = string.Join(", ", exceededCounters);
                if (throwWhenUnhealthy)
                {
                    throw new InvalidOperationException($"Host thresholds exceeded: [{formattedCounters}]");
                }
                return false;
            }

            return true;
        }

        public async Task<bool> DelayUntilHostReady(int timeoutSeconds = HostCheckTimeoutSeconds, int pollingIntervalMilliseconds = HostCheckPollingIntervalMilliseconds)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);
            TimeSpan delay = TimeSpan.FromMilliseconds(pollingIntervalMilliseconds);
            TimeSpan timeWaited = TimeSpan.Zero;

            while (!CanInvoke() && State != ScriptHostState.Error && (timeWaited < timeout))
            {
                await Task.Delay(delay);
                timeWaited += delay;
            }

            return CanInvoke();
        }
    }
}