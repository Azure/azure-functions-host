// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Class encapsulating a <see cref="ScriptHost"/> an keeping a singleton
    /// instance always alive, restarting as necessary.
    /// </summary>
    public class ScriptHostManager : IDisposable
    {
        private readonly ScriptHostConfiguration _config;
        private ScriptHost _instance;
        private bool _stopped;

        public ScriptHostManager(ScriptHostConfiguration config)
        {
            _config = config;
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

                _config.HostConfig = new JobHostConfiguration();
                ScriptHost newInstance = ScriptHost.Create(_config);

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
                _instance.RestartEvent.WaitOne();

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

        public void Dispose()
        {
            if (_instance != null)
            {
                _instance.Dispose();
            }
        }
    }
}
