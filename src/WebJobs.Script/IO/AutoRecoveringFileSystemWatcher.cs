// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script.IO
{
    public class AutoRecoveringFileSystemWatcher : IDisposable
    {
        private readonly string _path;
        private readonly string _filter;
        private readonly bool _includeSubdirectories;
        private readonly WatcherChangeTypes _changeTypes;
        private readonly TraceWriter _traceWriter;
        private readonly Action<ErrorEventArgs> _handleFileError;
        private readonly CancellationToken _cancellationToken;
        private readonly object _syncRoot = new object();
        private CancellationTokenSource _cancellationTokenSource;
        private FileSystemWatcher _fileWatcher;
        private bool _disposed = false;
        private int _recovering = 0;

        public event EventHandler<FileSystemEventArgs> Changed;

        public AutoRecoveringFileSystemWatcher(string path, string filter = "*.*",
            bool includeSubdirectories = true, WatcherChangeTypes changeTypes = WatcherChangeTypes.All, TraceWriter traceWriter = null)
        {
            _path = path;
            _filter = filter;
            _changeTypes = changeTypes;
            _includeSubdirectories = includeSubdirectories;
            _traceWriter = traceWriter;
            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;
            _handleFileError = new Action<ErrorEventArgs>(OnFileWatcherError).Debounce();

            InitializeWatcher();
        }

        ~AutoRecoveringFileSystemWatcher()
        {
            Dispose(false);
        }

        private void InitializeWatcher()
        {
            if (!Directory.Exists(_path))
            {
                throw new DirectoryNotFoundException($"The path '{_path}' cannot be found.");
            }

            lock (_syncRoot)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                _fileWatcher = new FileSystemWatcher(_path, _filter)
                {
                    IncludeSubdirectories = _includeSubdirectories,
                    EnableRaisingEvents = true
                };

                _fileWatcher.Error += (s, a) => _handleFileError(a);
                AddEventSubscriptions(_fileWatcher, _changeTypes);
            }
        }

        private void AddEventSubscriptions(FileSystemWatcher fileWatcher, WatcherChangeTypes changeTypes)
        {
            if (changeTypes.HasFlag(WatcherChangeTypes.Changed))
            {
                fileWatcher.Changed += OnFileChanged;
            }

            if (changeTypes.HasFlag(WatcherChangeTypes.Created))
            {
                fileWatcher.Created += OnFileChanged;
            }

            if (changeTypes.HasFlag(WatcherChangeTypes.Deleted))
            {
                fileWatcher.Deleted += OnFileChanged;
            }

            if (changeTypes.HasFlag(WatcherChangeTypes.Renamed))
            {
                fileWatcher.Renamed += OnFileChanged;
            }
        }

        protected void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            Changed?.Invoke(this, e);
        }

        protected void OnFileWatcherError(ErrorEventArgs args)
        {
            if (Interlocked.CompareExchange(ref _recovering, 1, 0) != 0)
            {
                return;
            }

            string errorMessage = args.GetException()?.Message ?? "Unknown";
            Trace($"Failure detected '{errorMessage}'. Initiating recovery...", TraceLevel.Warning);

            Recover().ContinueWith(t =>
            {
                if (t.IsFaulted || t.IsCanceled)
                {
                    t.Exception?.Handle(e => true);
                    Trace($"Recovery process aborted.", TraceLevel.Error);
                }
                else
                {
                    Trace("File watcher recovered.", TraceLevel.Info);
                }

                Interlocked.Exchange(ref _recovering, 0);
            });
        }

        private void Trace(string message, TraceLevel level)
        {
            _traceWriter?.Trace($"File watcher: ('{_path}') - {message}", level, null);
        }

        private async Task Recover(int attempt = 1)
        {
            // Exponential backoff on retries
            long wait = Convert.ToInt64((Math.Pow(2, attempt) - 1) / 2);

            // maximum wait of 5 minutes
            wait = Math.Min(wait, 300);

            if (wait > 0)
            {
                Trace($"Next recovery attempt in {wait} seconds...", TraceLevel.Warning);
                await Task.Delay(TimeSpan.FromSeconds(wait)).ConfigureAwait(false);
            }

            // Check if cancellation was requested while we were waiting
            _cancellationToken.ThrowIfCancellationRequested();

            try
            {
                ReleaseCurrentFileWatcher();
                InitializeWatcher();
            }
            catch (Exception exc) when (!(exc is TaskCanceledException) && !exc.IsFatal())
            {
                Trace($"Unable to recover - {exc.ToString()}", TraceLevel.Error);

                await Recover(++attempt);
            }
        }

        private void ReleaseCurrentFileWatcher()
        {
            FileSystemWatcher watcher;
            lock (_syncRoot)
            {
                watcher = _fileWatcher;
                _fileWatcher = null;
            }

            if (watcher != null)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();

                    ReleaseCurrentFileWatcher();
                }

                _cancellationTokenSource = null;
                _fileWatcher = null;
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
