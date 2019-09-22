// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.File;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public class AppMonitoringService : IHostedService, IDisposable
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _appOptionsMonitor;
        private readonly IScriptEventManager _eventManager;
        private readonly ILogger _logger;
        private readonly IList<IDisposable> _eventSubscriptions = new List<IDisposable>();
        private ScriptApplicationHostOptions _currentAppOptions;
        private bool _disposed = false;
        private FileWatcherEventSource _fileEventSource;

        public AppMonitoringService(IOptionsMonitor<ScriptApplicationHostOptions> appOptionsMonitor, ILoggerFactory loggerFactory, IScriptEventManager eventManager)
        {
            _appOptionsMonitor = appOptionsMonitor;
            _currentAppOptions = appOptionsMonitor.CurrentValue;
            _eventManager = eventManager;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);

            _appOptionsMonitor.OnChange((newOptions) =>
            {
                _currentAppOptions = newOptions;
                ReinitializeFileWatchers();
            });
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

        private void InitializeFileWatchers()
        {
            _fileEventSource = new FileWatcherEventSource(_eventManager, EventSources.ScriptFiles, _currentAppOptions.ScriptPath);

            // Right now, at app (webhost) level, we only care about app_offline.htm
            // This is because the state of the application is affected by this file.
            // And this state is determined before we start the ScriptHost.
            _eventSubscriptions.Add(_eventManager.OfType<FileEvent>()
                .Where(e => e.FileChangeArguments.Name.Equals(ScriptConstants.AppOfflineFileName, StringComparison.OrdinalIgnoreCase))
                .Subscribe(e => OnAppOfflineChange(e.FileChangeArguments)));

            _logger.LogInformation("File event source initialized.");
        }

        private void ReinitializeFileWatchers()
        {
            DisposeSubscriptions();
            _eventSubscriptions.Clear();

            InitializeFileWatchers();
            _logger.LogInformation("App monitoring service is reloaded.");
        }

        private void OnAppOfflineChange(FileSystemEventArgs e)
        {
            bool shutdown = File.Exists(e.FullPath);
            FileChangeHelper.TraceFileChangeRestart(_logger, e.ChangeType.ToString(), changeType: "File", e.FullPath, isShutdown: shutdown);

            if (shutdown)
            {
                FileChangeHelper.SignalShutdown(_eventManager, EventSources.AppMonitoring, shouldDebounce: false);
            }
            else
            {
                FileChangeHelper.SignalRestart(_eventManager, EventSources.AppMonitoring);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    DisposeSubscriptions();
                }

                _disposed = true;
                _logger.LogInformation("App monitoring service is disposed.");
            }
        }

        public void Dispose() => Dispose(true);

        private void DisposeSubscriptions()
        {
            _fileEventSource?.Dispose();

            foreach (var subscription in _eventSubscriptions)
            {
                subscription.Dispose();
            }
        }
    }
}
