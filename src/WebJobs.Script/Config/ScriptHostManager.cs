using System.Threading;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Class encapsulating a <see cref="ScriptHost"/> an keeping a singleton
    /// instance always alive, restarting as necessary.
    /// </summary>
    public class ScriptHostManager
    {
        private readonly ScriptHostConfiguration _config;
        private ScriptHost _instance;

        public ScriptHostManager(ScriptHostConfiguration config)
        {
            _config = config;
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
                _instance = ScriptHost.Create(_config);

                OnHostCreated();

                _instance.RunAndBlock();
            }
            while (_instance.Restart);
        }

        protected virtual void OnHostCreated()
        {
        }
    }
}
