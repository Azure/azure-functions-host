﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Class encapsulating a <see cref="ScriptHost"/> an keeping a singleton
    /// instance always alive, restarting as necessary.
    /// </summary>
    public class ScriptHostManager : IDisposable
    {
        private readonly ScriptHostConfiguration _config;
        private ScriptHost _currentInstance;

        // ScriptHosts are not thread safe, so be clear that only 1 thread at a time operates on each instance. 
        // List of all outstanding ScriptHost instances. Only 1 of these (specified by _currentInstance)
        // should be listening at a time. The others are "orphaned" and exist to finish executing any functions 
        // and will then remove themselves from this list. 
        private HashSet<ScriptHost> _liveInstances = new HashSet<ScriptHost>();

        private bool _stopped;
        private AutoResetEvent _stopEvent = new AutoResetEvent(false);

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
                return _currentInstance;
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

                // TODO: consider using StartAsync here to speed up
                // restarts.
                newInstance.Start();
                lock (_liveInstances)
                {
                    _liveInstances.Add(newInstance);
                }
                _currentInstance = newInstance;
                OnHostStarted();

                // only after ALL initialization is complete do we set this flag
                IsRunning = true;

                // Wait for a restart signal. This event will automatically reset.
                // While we're restarting, it is possible for another restart to be
                // signaled. That is fine - the restart will be processed immediately
                // once we get to this line again. The important thing is that these
                // restarts are only happening on a single thread.
                WaitHandle.WaitAny(new WaitHandle[] {
                    newInstance.RestartEvent,
                    _stopEvent
                });

                // Orphan the current host instance. We're stopping it, so it won't listen for any new functions
                // it will finish any currently executing functions and then clean itself up.
                // Spin around and create a new host instance.
                Task tIgnore = Orphan(newInstance);
            }
            while (!_stopped);
        }

        // Let the existing host instance finish currently executing functions.
        private async Task Orphan(ScriptHost instance)
        {
            lock (_liveInstances)
            {
                bool removed = _liveInstances.Remove(instance);
                if (!removed)
                {
                    return; // somebody else is handling it
                }
            }

            // this thread now owns the instance
            await instance.StopAsync();
            instance.Dispose();
        }

        public void Stop()
        {
            _stopped = true;

            try
            {
                _stopEvent.Set();
                ScriptHost[] instances = GetLiveInstancesAndClear();

                Task[] tasksStop = Array.ConvertAll(instances, instance => instance.StopAsync());
                Task.WaitAll(tasksStop);

                foreach (var instance in instances)
                {
                    instance.Dispose();
                }
            }
            catch
            {
                // best effort
            }
        }

        private ScriptHost[] GetLiveInstancesAndClear()
        {
            ScriptHost[] instances;
            lock (_liveInstances)
            {
                instances = _liveInstances.ToArray();
                _liveInstances.Clear();
            }

            return instances;
        }

        protected virtual void OnHostStarted()
        {
        }

        public void Dispose()
        {
            ScriptHost[] instances = GetLiveInstancesAndClear();
            foreach (var instance in instances)
            {
                instance.Dispose();
            }
        }
    }
}
