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
        private readonly ScriptHostConfiguration _config;
        private readonly TraceWriter _traceWriter;
        private ScriptHost _instance;
        private FileSystemWatcher _fileWatcher;
        private int _directoryCountSnapshot;
        private bool _restarting;

        public ScriptHostManager(ScriptHostConfiguration config)
        {
            _config = config;
            _traceWriter = config.TraceWriter;

            if (_config.WatchFiles)
            {
                _fileWatcher = new FileSystemWatcher(_config.RootPath)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                _fileWatcher.Changed += OnConfigurationFileChanged;
                _fileWatcher.Created += OnConfigurationFileChanged;
                _fileWatcher.Deleted += OnConfigurationFileChanged;
                _fileWatcher.Renamed += OnConfigurationFileChanged;
            }
        }

        public ScriptHost Instance
        {
            get
            {
                return _instance;
            }
        }

        public void StartAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // Start the host and restart it if requested. Restarts will happen when
            // script files change, configuration changes, etc.
            _instance = null;
            do
            {
                _traceWriter.Verbose("Starting Host...");

                _config.HostConfig = new JobHostConfiguration();
                _instance = ScriptHost.Create(_config);

                OnHostCreated();

                // take a snapshot so we can detect function additions/removals
                _directoryCountSnapshot = Directory.EnumerateDirectories(_config.RootPath).Count();
                _restarting = false;

                _instance.RunAndBlock();

                if (_restarting)
                {
                    // When restarting due to file changes, often this will be due to a short
                    // burst of file events (e.g. adding a new function directory with multiple files).
                    // We want to allow those to finish so we only restart once after all the operations
                    // are complete
                    Thread.Sleep(500);
                }
            }
            while (true);
        }

        protected virtual void OnHostCreated()
        {
        }

        private void OnConfigurationFileChanged(object sender, FileSystemEventArgs e)
        {
            string fileName = Path.GetFileName(e.Name);
            if (!_restarting &&
                ((string.Compare(fileName, "host.json") == 0) || string.Compare(fileName, "function.json") == 0) ||
                ((Directory.EnumerateDirectories(_config.RootPath).Count() != _directoryCountSnapshot)))
            {
                _traceWriter.Verbose(string.Format("File change of type '{0}' detected for file '{1}'", e.ChangeType, e.FullPath));
                StopAndRestart();
            }
        }

        private void StopAndRestart()
        {
            if (_restarting)
            {
                // we've already received a restart call
                return;
            }

            _traceWriter.Verbose("Host configuration has changed. Restarting.");

            // Flag for restart and stop the host.
            _restarting = true;
            Instance.Stop();
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
