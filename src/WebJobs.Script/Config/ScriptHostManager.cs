using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Class encapsulating a <see cref="ScriptHost"/> an keeping a singleton
    /// instance always alive, restarting as necessary.
    /// </summary>
    public class ScriptHostManager : IDisposable
    {
        private static AutoResetEvent _autoResetEvent = new AutoResetEvent(false);
        private readonly ScriptHostConfiguration _config;
        private readonly TraceWriter _traceWriter;
        private ScriptHost _instance;
        private FileSystemWatcher _fileWatcher;
        private int _directoryCountSnapshot;
        private Action<FileSystemEventArgs> _restart;
        private bool _stopped;

        public ScriptHostManager(ScriptHostConfiguration config)
        {
            _config = config;
            _config.TraceWriter = _config.TraceWriter ?? new NullTraceWriter();
            _traceWriter = config.TraceWriter;

            if (_config.WatchFiles)
            {
                _fileWatcher = new FileSystemWatcher(_config.RootScriptPath)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                _fileWatcher.Changed += OnConfigurationFileChanged;
                _fileWatcher.Created += OnConfigurationFileChanged;
                _fileWatcher.Deleted += OnConfigurationFileChanged;
                _fileWatcher.Renamed += OnConfigurationFileChanged;
            }

            // If a file change should result in a restart, we debounce the event to
            // ensure that only a single restart is triggered within a specific time window.
            // This allows us to deal with a large set of file change events that might
            // result from a bulk copy/unzip operation. In such cases, we only want to
            // restart after ALL the operations are complete and there is a quiet period.
            _restart = (e) =>
            {
                _traceWriter.Verbose(string.Format("File change of type '{0}' detected for file '{1}'", e.ChangeType, e.FullPath));
                _traceWriter.Verbose("Host configuration has changed. Restarting.");

                // signal host restart
                _autoResetEvent.Set();
            };
            _restart = _restart.Debounce(500);
        }

        /// <summary>
        /// Returns true if the <see cref="ScriptHost"/> is up and running and ready to
        /// process requests.
        /// </summary>
        public bool IsRunning { get; private set; }

        public ScriptHost Instance
        {
            get
            {
                return _instance;
            }
        }

        public void RunAndBlock(CancellationToken cancellationToken = default(CancellationToken))
        {
            // Start the host and restart it if requested. Host Restarts will happen when
            // host level configuration files change
            do
            {
                IsRunning = false;
                _traceWriter.Verbose("Starting Host...");

                _config.HostConfig = new JobHostConfiguration();
                ScriptHost newInstance = ScriptHost.Create(_config);

                // take a snapshot so we can detect function additions/removals
                _directoryCountSnapshot = Directory.EnumerateDirectories(_config.RootScriptPath).Count();

                newInstance.Start();
                _instance = newInstance;
                OnHostStarted();

                // only after ALL initialization is complete do we set this flag
                IsRunning = true;

                // Wait for a restart signal. This event will automatically reset.
                // While we're restarting, it is possible for another restart to be
                // signaled. That is fine - the restart will be processed immediately
                // once we get to this line again. The important thing is that these
                // restarts are only happening on a single thread.
                _autoResetEvent.WaitOne();

                // stop the host fully
                _instance.Stop();
                _instance.Dispose();
            }
            while (!_stopped);
        }

        public void Stop()
        {
            _stopped = true;

            try
            {
                if (_instance != null)
                {
                    _instance.Stop();
                    _instance.Dispose();
                }
            }
            catch
            {
                // best effort
            }  
        }

        protected virtual void OnHostStarted()
        {
        }

        private void OnConfigurationFileChanged(object sender, FileSystemEventArgs e)
        {
            string fileName = Path.GetFileName(e.Name);

            if (((string.Compare(fileName, "host.json") == 0) || string.Compare(fileName, "function.json") == 0) ||
                ((Directory.EnumerateDirectories(_config.RootScriptPath).Count() != _directoryCountSnapshot)))
            {
                _restart(e);
            }
        }

        public void Dispose()
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.Dispose();
            }
        }
    }
}
