// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Eventing.File
{
    public sealed class FileWatcherEventSource : IDisposable
    {
        private readonly AutoRecoveringFileSystemWatcher _fileWatcher;
        private readonly IScriptEventManager _eventManager;
        private readonly string _source;
        private readonly object _syncLock = new object();
        private bool _disposed = false;

        public FileWatcherEventSource(IScriptEventManager eventManager,
            string source,
            string path,
            string filter = "*.*",
            bool includeSubdirectories = true,
            WatcherChangeTypes changeTypes = WatcherChangeTypes.All,
            ILoggerFactory loggerFactory = null)
        {
            _source = source;
            _eventManager = eventManager;
            _fileWatcher = new AutoRecoveringFileSystemWatcher(path, filter, includeSubdirectories, changeTypes, loggerFactory);
            _fileWatcher.Changed += FileChanged;
        }

        private void FileChanged(object sender, FileSystemEventArgs e)
        {
            lock (_syncLock)
            {
                if (_disposed)
                {
                    return;
                }

                var fileEvent = new FileEvent(_source, e);
                try
                {
                    _eventManager.Publish(fileEvent);
                }
                catch (ObjectDisposedException ex) when (string.Equals(ex.ObjectName, nameof(ScriptEventManager), StringComparison.Ordinal))
                {
                }
            }
        }

        private void Dispose(bool disposing)
        {
            bool disposeWatcher = false;

            lock (_syncLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (disposing)
                {
                    _fileWatcher.Changed -= FileChanged;
                    disposeWatcher = true;
                }
            }

            if (disposeWatcher)
            {
                _fileWatcher.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
