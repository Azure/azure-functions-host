// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class FileMonitoringService : IHostedService, IDisposable
    {
        private readonly ScriptHostOptions _scriptOptions;
        private readonly IScriptEventManager _eventManager;
        private readonly IScriptHostEnvironment _scriptEnvironment;
        private readonly string _hostLogPath;
        private readonly ILogger _logger;
        private readonly IList<IDisposable> _eventSubscriptions = new List<IDisposable>();
        private readonly Func<Task> _restart;
        private readonly Action _shutdown;
        private AutoRecoveringFileSystemWatcher _debugModeFileWatcher;
        private FileWatcherEventSource _fileEventSource;
        private bool _shutdownScheduled;
        private bool _disposed = false;

        public FileMonitoringService(IOptions<ScriptHostOptions> scriptOptions, ILoggerFactory loggerFactory, IScriptEventManager eventManager, IScriptHostEnvironment scriptEnvironment)
        {
            _scriptOptions = scriptOptions.Value;
            _eventManager = eventManager;
            _scriptEnvironment = scriptEnvironment;
            _hostLogPath = Path.Combine(_scriptOptions.RootLogPath, "Host");
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);

            // If a file change should result in a restart, we debounce the event to
            // ensure that only a single restart is triggered within a specific time window.
            // This allows us to deal with a large set of file change events that might
            // result from a bulk copy/unzip operation. In such cases, we only want to
            // restart after ALL the operations are complete and there is a quiet period.
            _restart = RestartAsync;
            _restart = _restart.Debounce(500);

            _shutdown = Shutdown;
            _shutdown = _shutdown.Debounce(500);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            InitializeFileWatchers();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Initialize file and directory change monitoring.
        /// </summary>
        private void InitializeFileWatchers()
        {
            _debugModeFileWatcher = new AutoRecoveringFileSystemWatcher(_hostLogPath, ScriptConstants.DebugSentinelFileName,
                   includeSubdirectories: false, changeTypes: WatcherChangeTypes.Created | WatcherChangeTypes.Changed);

            _debugModeFileWatcher.Changed += OnDebugModeFileChanged;
            _logger.LogTrace("Debug file watch initialized.");

            if (_scriptOptions.FileWatchingEnabled)
            {
                _fileEventSource = new FileWatcherEventSource(_eventManager, EventSources.ScriptFiles, _scriptOptions.RootScriptPath);

                _eventSubscriptions.Add(_eventManager.OfType<FileEvent>()
                        .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal))
                        .Subscribe(e => OnFileChanged(e.FileChangeArguments)));

                _logger.LogTrace("File event source initialized.");
            }

            _eventSubscriptions.Add(_eventManager.OfType<HostRestartEvent>()
                    .Subscribe((msg) => ScheduleRestartAsync(false)
                    .ContinueWith(t => _logger.LogCritical(t.Exception.Message),
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)));
        }

        /// <summary>
        /// Whenever the debug marker file changes we update our debug timeout
        /// </summary>
        private void OnDebugModeFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!_disposed)
            {
                _eventManager.Publish(new DebugNotification(nameof(FileMonitoringService), DateTime.UtcNow));
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

            if (_scriptOptions.WatchDirectories.Contains(directory))
            {
                changeDescription = "Watched directory";
            }
            else if (string.Compare(fileName, ScriptConstants.HostMetadataFileName, StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(fileName, ScriptConstants.FunctionMetadataFileName, StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(fileName, ScriptConstants.ProxyMetadataFileName, StringComparison.OrdinalIgnoreCase) == 0)
            {
                changeDescription = "File";
            }
            else if ((e.ChangeType == WatcherChangeTypes.Deleted || Directory.Exists(e.FullPath))
                && !_scriptOptions.RootScriptDirectorySnapshot.SequenceEqual(Directory.EnumerateDirectories(_scriptOptions.RootScriptPath)))
            {
                // Check directory snapshot only if "Deleted" change or if directory changed
                changeDescription = "Directory";
            }

            if (!string.IsNullOrEmpty(changeDescription))
            {
                bool shutdown = false;
                string fileExtension = Path.GetExtension(fileName);
                if (!string.IsNullOrEmpty(fileExtension) && ScriptConstants.AssemblyFileTypes.Contains(fileExtension, StringComparer.OrdinalIgnoreCase))
                {
                    shutdown = true;
                }

                TraceFileChangeRestart(changeDescription, e.ChangeType.ToString(), e.FullPath, shutdown);
                ScheduleRestartAsync(shutdown).ContinueWith(t => _logger.LogError($"Error restarting host (full shutdown: {shutdown})", t.Exception),
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

        private async Task ScheduleRestartAsync(bool shutdown)
        {
            if (shutdown)
            {
                _shutdownScheduled = true;
                _shutdown();
            }
            else
            {
                await _restart();
            }
        }

        private Task RestartAsync()
        {
            if (!_shutdownScheduled)
            {
                _scriptEnvironment.RestartHost();
            }

            return Task.CompletedTask;
        }

        private void Shutdown()
        {
            _scriptEnvironment.Shutdown();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _fileEventSource?.Dispose();

                    if (_debugModeFileWatcher != null)
                    {
                        _debugModeFileWatcher.Changed -= OnDebugModeFileChanged;
                        _debugModeFileWatcher.Dispose();
                    }

                    foreach (var subscription in _eventSubscriptions)
                    {
                        subscription.Dispose();
                    }
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
