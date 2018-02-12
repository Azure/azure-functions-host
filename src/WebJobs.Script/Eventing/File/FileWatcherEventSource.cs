// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.IO;

namespace Microsoft.Azure.WebJobs.Script.Eventing.File
{
    public sealed class FileWatcherEventSource : IDisposable
    {
        private readonly AutoRecoveringFileSystemWatcher _fileWatcher;
        private readonly IScriptEventManager _eventManager;
        private readonly string _source;
        private readonly TraceWriter _traceWriter;
        private bool _disposed = false;

        public FileWatcherEventSource(IScriptEventManager eventManager,
            string source,
            string path,
            string filter = "*.*",
            bool includeSubdirectories = true,
            WatcherChangeTypes changeTypes = WatcherChangeTypes.All,
            TraceWriter traceWriter = null)
        {
            _source = source;
            _eventManager = eventManager;
            _fileWatcher = new AutoRecoveringFileSystemWatcher(path, filter, includeSubdirectories, changeTypes, traceWriter);
            _fileWatcher.Changed += FileChanged;
            _traceWriter = traceWriter;
        }

        private void FileChanged(object sender, FileSystemEventArgs e)
        {
            // This handler is called on a background thread, so any exceptions thrown will crash
            // the process. Handle and log all errors instead.
            try
            {
                var fileEvent = new FileEvent(_source, e);
                _eventManager.Publish(fileEvent);
            }
            catch (Exception ex)
            {
                _traceWriter?.Error($"Error handling '{e.ChangeType}' notification for '{e.FullPath}'.", ex);
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _fileWatcher.Dispose();
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
