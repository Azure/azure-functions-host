﻿using System;
using System.Collections.Generic;
﻿using System.Globalization;
﻿using System.Reflection;
using System.Threading;
﻿using Microsoft.Azure.Jobs.Host;
using Microsoft.Azure.Jobs.Host.Protocols;
using System.IO;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Helper class for providing a cancellation token for when this WebJob's shutdown is signaled. 
    /// </summary>
    public sealed class WebjobsShutdownWatcher : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly string _shutdownFile;
        private FileSystemWatcher _watcher;

        /// <summary>
        /// Begin watching for a shutdown notification from Antares. 
        /// </summary>
        public WebjobsShutdownWatcher()
        {
            // http://blog.amitapple.com/post/2014/05/webjobs-graceful-shutdown/#.U3aIXRFOVaQ
            // Antares will set this file to signify shutdown 
            _shutdownFile = Environment.GetEnvironmentVariable("WEBJOBS_SHUTDOWN_FILE");
            if (_shutdownFile == null)
            {
                // If env var is not set, then no shutdown support
                return;
            }

            _cts = new CancellationTokenSource();

            // Setup a file system watcher on that file's directory to know when the file is created
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(_shutdownFile));
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.LastWrite;
            _watcher.IncludeSubdirectories = false;
            _watcher.EnableRaisingEvents = true;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (e.FullPath.IndexOf(Path.GetFileName(_shutdownFile), StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Found the file mark this WebJob as finished
                _cts.Cancel();
            }
        }

        /// <summary>
        /// Get a CancellationToken that is signaled when the shutdown notification is detected.
        /// </summary>
        public CancellationToken Token
        {
            get
            {
                // CancellationToken.None means CanBeCancelled = false, which can facilitate optimizations with tokens. 
                return (_cts != null) ? _cts.Token : CancellationToken.None;
            }
        }

        /// <summary>
        /// Stop watching for the shutdown notification
        /// </summary>
        public void Dispose()
        {
            if (_watcher != null)
            {
                if (_cts != null)
                {
                    _cts.Dispose();
                }
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }
}