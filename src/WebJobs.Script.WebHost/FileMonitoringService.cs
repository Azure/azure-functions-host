// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.File;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class FileMonitoringService : IFileMonitoringService, IDisposable
    {
        private readonly ScriptJobHostOptions _scriptOptions;
        private readonly IScriptEventManager _eventManager;
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IEnvironment _environment;
        private readonly string _hostLogPath;
        private readonly ILogger _logger;
        private readonly ILogger<FileMonitoringService> _typedLogger;
        private readonly IList<IDisposable> _eventSubscriptions = new List<IDisposable>();
        private readonly Func<Task> _restart;
        private readonly Action _shutdown;
        private readonly ImmutableArray<string> _rootDirectorySnapshot;
        private AutoRecoveringFileSystemWatcher _debugModeFileWatcher;
        private AutoRecoveringFileSystemWatcher _diagnosticModeFileWatcher;
        private FileWatcherEventSource _fileEventSource;
        private bool _restartScheduled;
        private bool _shutdownScheduled;
        private long _restartRequested;
        private bool _disposed = false;
        private bool _watchersStopped = false;
        private object _stopWatchersLock = new object();
        private long _suspensionRequestsCount = 0;

        public FileMonitoringService(IOptions<ScriptJobHostOptions> scriptOptions, ILoggerFactory loggerFactory, IScriptEventManager eventManager, IApplicationLifetime applicationLifetime, IScriptHostManager scriptHostManager, IEnvironment environment)
        {
            _scriptOptions = scriptOptions.Value;
            _eventManager = eventManager;
            _applicationLifetime = applicationLifetime;
            _scriptHostManager = scriptHostManager;
            _hostLogPath = Path.Combine(_scriptOptions.RootLogPath, "Host");
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _environment = environment;

            // Use this for newer logs as we can't change existing categories of log messages
            _typedLogger = loggerFactory.CreateLogger<FileMonitoringService>();

            // If a file change should result in a restart, we debounce the event to
            // ensure that only a single restart is triggered within a specific time window.
            // This allows us to deal with a large set of file change events that might
            // result from a bulk copy/unzip operation. In such cases, we only want to
            // restart after ALL the operations are complete and there is a quiet period.
            _restart = RestartAsync;
            _restart = _restart.Debounce(500);

            _shutdown = Shutdown;
            _shutdown = _shutdown.Debounce(milliseconds: 500);
            _rootDirectorySnapshot = GetDirectorySnapshot();
        }

        internal ImmutableArray<string> GetDirectorySnapshot()
        {
            if (_scriptOptions.RootScriptPath != null)
            {
                try
                {
                    return Directory.EnumerateDirectories(_scriptOptions.RootScriptPath).ToImmutableArray();
                }
                catch (DirectoryNotFoundException)
                {
                    _logger.LogInformation($"Unable to get directory snapshot. No directory present at {_scriptOptions.RootScriptPath}");
                }
            }
            return ImmutableArray<string>.Empty;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            InitializeFileWatchers();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopFileWatchers();
            return Task.CompletedTask;
        }

        public IDisposable SuspendRestart(bool autoRestart)
        {
            return new SuspendRestartRequest(this, autoRestart);
        }

        private void ResumeRestartIfScheduled()
        {
            if (_restartScheduled)
            {
                using (System.Threading.ExecutionContext.SuppressFlow())
                {
                    _typedLogger.LogDebug("Resuming scheduled restart.");
                    Task.Run(async () => await ScheduleRestartAsync());
                }
            }
        }

        private async Task ScheduleRestartAsync(bool shutdown)
        {
            _restartScheduled = true;
            if (shutdown)
            {
                _shutdownScheduled = true;
            }

            await ScheduleRestartAsync();
        }

        private async Task ScheduleRestartAsync()
        {
            if (Interlocked.Read(ref _suspensionRequestsCount) > 0)
            {
                _logger.LogDebug("Restart requested while currently suspended. Ignoring request.");
            }
            else
            {
                if (_shutdownScheduled)
                {
                    _shutdown();
                }
                else
                {
                    await _restart();
                }
            }
        }

        /// <summary>
        /// Initialize file and directory change monitoring.
        /// </summary>
        private void InitializeFileWatchers()
        {
            if (_scriptOptions.FileWatchingEnabled)
            {
                _fileEventSource = new FileWatcherEventSource(_eventManager, EventSources.ScriptFiles, _scriptOptions.RootScriptPath);

                _eventSubscriptions.Add(_eventManager.OfType<FileEvent>()
                        .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal))
                        .Subscribe(e => OnFileChanged(e.FileChangeArguments)));

                _logger.LogDebug("File event source initialized.");
            }

            _eventSubscriptions.Add(_eventManager.OfType<HostRestartEvent>()
                    .Subscribe((msg) => ScheduleRestartAsync(false)
                    .ContinueWith(t => _logger.LogCritical(t.Exception.Message),
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)));

            // Delay starting up for logging and debug file watchers to avoid long start up times
            Utility.ExecuteAfterColdStartDelay(_environment, InitializeSecondaryFileWatchers);
        }

        /// <summary>
        /// Initializes the file and directory monitoring that does not need to happen as part of a Host startup
        /// These watchers can be started after a delay to avoid startup performance issue
        /// </summary>
        private void InitializeSecondaryFileWatchers()
        {
            if (_watchersStopped)
            {
                return;
            }

            lock (_stopWatchersLock)
            {
                if (!_watchersStopped)
                {
                    FileUtility.EnsureDirectoryExists(_hostLogPath);

                    _debugModeFileWatcher = new AutoRecoveringFileSystemWatcher(_hostLogPath, ScriptConstants.DebugSentinelFileName,
                            includeSubdirectories: false, changeTypes: WatcherChangeTypes.Created | WatcherChangeTypes.Changed);
                    _debugModeFileWatcher.Changed += OnDebugModeFileChanged;
                    _logger.LogDebug("Debug file watch initialized.");

                    _diagnosticModeFileWatcher = new AutoRecoveringFileSystemWatcher(_hostLogPath, ScriptConstants.DiagnosticSentinelFileName,
                           includeSubdirectories: false, changeTypes: WatcherChangeTypes.Created | WatcherChangeTypes.Changed);
                    _diagnosticModeFileWatcher.Changed += OnDiagnosticModeFileChanged;
                    _logger.LogDebug("Diagnostic file watch initialized.");
                }
            }
        }

        private void StopFileWatchers()
        {
            if (_watchersStopped)
            {
                return;
            }

            lock (_stopWatchersLock)
            {
                if (_watchersStopped)
                {
                    return;
                }

                _typedLogger.LogDebug("Stopping file watchers.");

                _fileEventSource?.Dispose();

                if (_debugModeFileWatcher != null)
                {
                    _debugModeFileWatcher.Changed -= OnDebugModeFileChanged;
                    _debugModeFileWatcher.Dispose();
                }

                if (_diagnosticModeFileWatcher != null)
                {
                    _diagnosticModeFileWatcher.Changed -= OnDiagnosticModeFileChanged;
                    _diagnosticModeFileWatcher.Dispose();
                }

                foreach (var subscription in _eventSubscriptions)
                {
                    subscription.Dispose();
                }

                _watchersStopped = true;
            }
        }

        /// <summary>
        /// Whenever the debug sentinel file changes we update our debug timeout
        /// </summary>
        private void OnDebugModeFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!_disposed)
            {
                _eventManager.Publish(new DebugNotification(nameof(FileMonitoringService), DateTime.UtcNow));
            }
        }

        /// <summary>
        /// Whenever the diagnostic sentinel file changes we update our debug timeout
        /// </summary>
        private void OnDiagnosticModeFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!_disposed)
            {
                _eventManager.Publish(new DiagnosticNotification(nameof(FileMonitoringService), DateTime.UtcNow));
            }
        }

        private void OnFileChanged(FileSystemEventArgs e)
        {
            // We will perform a host restart in the following cases:
            // - the file change was under one of the configured watched directories (e.g. node_modules, shared code directories, etc.)
            // - the host.json file was changed
            // - a function.json file was changed
            // - a proxies.json file was changed
            // - a function directory was added/removed/renamed
            // A full host shutdown is performed when an assembly (.dll, .exe) in a watched directory is modified

            string changeDescription = string.Empty;
            string directory = GetRelativeDirectory(e.FullPath, _scriptOptions.RootScriptPath);
            string fileName = Path.GetFileName(e.Name);
            bool shutdown = false;

            if (_scriptOptions.WatchDirectories.Contains(directory))
            {
                changeDescription = "Watched directory";
            }
            else if (string.Equals(fileName, ScriptConstants.AppOfflineFileName, StringComparison.OrdinalIgnoreCase))
            {
                // app_offline.htm has changed
                // when app_offline.htm is created, we trigger
                // a shutdown right away so when the host
                // starts back up it will be offline
                // when app_offline.htm is deleted, we trigger
                // a restart to bring the host back online
                changeDescription = "File";
                if (File.Exists(e.FullPath))
                {
                    TraceFileChangeRestart(changeDescription, e.ChangeType.ToString(), e.FullPath, isShutdown: true);
                    Shutdown();
                }
            }
            else if (_scriptOptions.WatchFiles.Any(f => string.Equals(fileName, f, StringComparison.OrdinalIgnoreCase)))
            {
                changeDescription = "File";
            }
            else if ((e.ChangeType == WatcherChangeTypes.Deleted || Directory.Exists(e.FullPath))
                && !_rootDirectorySnapshot.SequenceEqual(Directory.EnumerateDirectories(_scriptOptions.RootScriptPath)))
            {
                // Check directory snapshot only if "Deleted" change or if directory changed
                changeDescription = "Directory";
            }

            if (!string.IsNullOrEmpty(changeDescription))
            {
                string fileExtension = Path.GetExtension(fileName);
                if (!string.IsNullOrEmpty(fileExtension) && ScriptConstants.AssemblyFileTypes.Contains(fileExtension, StringComparer.OrdinalIgnoreCase))
                {
                    shutdown = true;
                }

                TraceFileChangeRestart(changeDescription, e.ChangeType.ToString(), e.FullPath, shutdown);
                ScheduleRestartAsync(shutdown).ContinueWith(t => _logger.LogError(t.Exception, $"Error restarting host (full shutdown: {shutdown})"),
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        private void TraceFileChangeRestart(string changeDescription, string changeType, string path, bool isShutdown)
        {
            string fileChangeMsg = string.Format(CultureInfo.InvariantCulture, "{0} change of type '{1}' detected for '{2}'", changeDescription, changeType, path);
            _logger.LogInformation(fileChangeMsg);

            string action = isShutdown ? "shutdown" : "restart";
            string signalMessage = $"Host configuration has changed. Signaling {action}";
            _logger.LogInformation(signalMessage);
        }

        internal static string GetRelativeDirectory(string path, string scriptRoot)
        {
            if (path.StartsWith(scriptRoot))
            {
                string directory = path.Substring(scriptRoot.Length).TrimStart(Path.DirectorySeparatorChar);
                int idx = directory.IndexOf(Path.DirectorySeparatorChar);
                if (idx != -1)
                {
                    directory = directory.Substring(0, idx);
                }

                return directory;
            }

            return string.Empty;
        }

        private Task RestartAsync()
        {
            if (!_shutdownScheduled && Interlocked.Exchange(ref _restartRequested, 1) == 0)
            {
                return _scriptHostManager.RestartHostAsync();
            }

            return Task.CompletedTask;
        }

        private void Shutdown()
        {
            _applicationLifetime.StopApplication();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    StopFileWatchers();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        internal static async Task SetAppOfflineState(string rootPath, bool offline)
        {
            string path = Path.Combine(rootPath, ScriptConstants.AppOfflineFileName);
            bool offlineFileExists = File.Exists(path);

            if (offline && !offlineFileExists)
            {
                // create the app_offline.htm file in the root script directory
                string content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.{ScriptConstants.AppOfflineFileName}");
                await FileUtility.WriteAsync(path, content);
            }
            else if (!offline && offlineFileExists)
            {
                // delete the app_offline.htm file
                await Utility.InvokeWithRetriesAsync(() =>
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }, maxRetries: 3, retryInterval: TimeSpan.FromSeconds(1));
            }
        }

        private class SuspendRestartRequest : IDisposable
        {
            private FileMonitoringService _fileMonitoringService;
            private bool _autoResume;
            private bool _disposed = false;

            public SuspendRestartRequest(FileMonitoringService fileMonitoringService, bool autoResume)
            {
                _fileMonitoringService = fileMonitoringService;
                _autoResume = autoResume;
                Interlocked.Increment(ref _fileMonitoringService._suspensionRequestsCount);
                _fileMonitoringService._typedLogger.LogDebug($"Entering restart suspension scope. ({_fileMonitoringService._suspensionRequestsCount} requests).");
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    Interlocked.Decrement(ref _fileMonitoringService._suspensionRequestsCount);
                    _fileMonitoringService._typedLogger.LogDebug($"Exiting restart suspension scope. ({_fileMonitoringService._suspensionRequestsCount} requests).");
                    if (_autoResume)
                    {
                        _fileMonitoringService.ResumeRestartIfScheduled();
                    }
                    _disposed = true;
                }
            }
        }
    }
}
