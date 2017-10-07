// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Class encapsulating a <see cref="ScriptHost"/> an keeping a singleton
    /// instance always alive, restarting as necessary.
    /// </summary>
    public class ScriptHostManager : IScriptHostEnvironment, IDisposable
    {
        private readonly ScriptHostConfiguration _config;
        private readonly IScriptHostFactory _scriptHostFactory;
        private readonly IScriptHostEnvironment _environment;
        private readonly IDisposable _fileEventSubscription;
        private readonly StructuredLogWriter _structuredLogWriter;
        private ScriptHost _currentInstance;

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

        public ScriptHostManager(ScriptHostConfiguration config, IScriptEventManager eventManager = null, IScriptHostEnvironment environment = null)
            : this(config, ScriptSettingsManager.Instance, new ScriptHostFactory(), eventManager, environment)
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
            IScriptHostEnvironment environment = null)
        {
            _environment = environment ?? this;
            _config = config;
            _settingsManager = settingsManager;
            _scriptHostFactory = scriptHostFactory;

            EventManager = eventManager ?? new ScriptEventManager();

            _structuredLogWriter = new StructuredLogWriter(EventManager, config.RootLogPath);
        }

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
            return State == ScriptHostState.Created || State == ScriptHostState.Running;
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

                    // Create a new host config, but keep the host id from existing one
                    _config.HostConfig = new JobHostConfiguration
                    {
                        HostId = _config.HostConfig.HostId
                    };
                    OnInitializeConfig(_config);
                    newInstance = _scriptHostFactory.Create(_environment, EventManager, _settingsManager, _config);
                    _traceWriter = newInstance.TraceWriter;
                    _logger = newInstance.Logger;

                    _currentInstance = newInstance;
                    lock (_liveInstances)
                    {
                        _liveInstances.Add(newInstance);
                    }

                    OnHostCreated();

                    string message = string.Format("Starting Host (HostId={0}, Version={1}, ProcessId={2}, Debug={3}, Attempt={4}, FunctionsExtensionVersion={5})",
                        newInstance.ScriptConfig.HostConfig.HostId, ScriptHost.Version, Process.GetCurrentProcess().Id, newInstance.InDebugMode.ToString(), consecutiveErrorCount,
                        _settingsManager.GetSetting(EnvironmentSettingNames.FunctionsExtensionVersion));
                    _traceWriter?.Info(message);
                    _logger?.LogInformation(message);

                    newInstance.StartAsync(cancellationToken).GetAwaiter().GetResult();

                    // log any function initialization errors
                    LogErrors(newInstance);

                    OnHostStarted();

                    // only after ALL initialization is complete do we set the
                    // state to Running
                    State = ScriptHostState.Running;
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

                    // Immediately set the state to Default, which causes CanInvoke() to return false.
                    State = ScriptHostState.Default;

                    // Orphan the current host instance. We're stopping it, so it won't listen for any new functions
                    // it will finish any currently executing functions and then clean itself up.
                    // Spin around and create a new host instance.
#pragma warning disable 4014
                    Orphan(newInstance);
#pragma warning restore 4014
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
                        Orphan(newInstance, forceStop: true)
                            .ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                {
                                    t.Exception.Handle(e => true);
                                }
                            }, TaskContinuationOptions.ExecuteSynchronously);
                    }

                    // attempt restarts using an exponential backoff strategy
                    CreateRestartBackoffDelay(consecutiveErrorCount).GetAwaiter().GetResult();
                }
            }
            while (!_stopped && !cancellationToken.IsCancellationRequested);
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

        protected virtual void OnHostCreated()
        {
            State = ScriptHostState.Created;
        }

        protected virtual void OnHostStarted()
        {
            var metricsLogger = _config.HostConfig.GetService<IMetricsLogger>();
            metricsLogger.LogEvent(new HostStarted(Instance));
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
            _restartHostEvent.Set();

            // we reset the state immediately here to ensure that
            // any external calls to CanInvoke will return false
            // immediately after the restart has been initiated
            State = ScriptHostState.Default;
        }

        public virtual void Shutdown()
        {
            Stop();

            Process.GetCurrentProcess().Close();
        }
    }
}
