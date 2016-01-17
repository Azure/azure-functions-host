using System.Threading;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptHostManager
    {
        private readonly ScriptHostConfiguration _config;
        private ScriptHost _host;

        public ScriptHostManager(ScriptHostConfiguration config)
        {
            _config = config;
        }

        public ScriptHost Instance
        {
            get
            {
                return _host;
            }
        }

        public void StartAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // Start the host and restart it if requested
            _host = null;
            do
            {
                _host = ScriptHost.Create(_config);

                OnHostCreated();

                _host.RunAndBlock();
            }
            while (_host.Restart);
        }

        protected virtual void OnHostCreated()
        {
        }
    }
}
