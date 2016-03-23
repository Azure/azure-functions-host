// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly IScriptHostFactory _scriptHostFactory;
        private ScriptHost _currentInstance;

        // ScriptHosts are not thread safe, so be clear that only 1 thread at a time operates on each instance. 
        // List of all outstanding ScriptHost instances. Only 1 of these (specified by _currentInstance)
        // should be listening at a time. The others are "orphaned" and exist to finish executing any functions 
        // and will then remove themselves from this list. 
        private HashSet<ScriptHost> _liveInstances = new HashSet<ScriptHost>();

        private bool _disposed;
        private bool _stopped;
        private AutoResetEvent _stopEvent = new AutoResetEvent(false);
        private TraceWriter _traceWriter;

        public ScriptHostManager(ScriptHostConfiguration config)
            : this(config, new ScriptHostFactory())
        {
        }

        public ScriptHostManager(ScriptHostConfiguration config, IScriptHostFactory scriptHostFactory)
        {
            _config = config;
            _scriptHostFactory = scriptHostFactory;
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

        /// <summary>
        /// Gets the last host <see cref="Exception"/> that has occurred.
        /// </summary>
        public Exception LastError { get; private set; }

        public void RunAndBlock(CancellationToken cancellationToken = default(CancellationToken))
        {
            // Start the host and restart it if requested. Host Restarts will happen when
            // host level configuration files change
            do
            {
                ScriptHost newInstance = null;
                try
                {
                    IsRunning = false;

                    // Create a new host config, but keep the host id from existing one
                    _config.HostConfig = new JobHostConfiguration
                    {
                        HostId = _config.HostConfig.HostId
                    };
                    OnInitializeConfig(_config.HostConfig);
                    newInstance = _scriptHostFactory.Create(_config);
                    _traceWriter = newInstance.TraceWriter;

                    newInstance.StartAsync(cancellationToken).Wait();

                    // write any function initialization errors to the log file
                    LogErrors(newInstance);

                    lock (_liveInstances)
                    {
                        _liveInstances.Add(newInstance);
                    }
                    _currentInstance = newInstance;
                    OnHostStarted();

                    // only after ALL initialization is complete do we set this flag
                    IsRunning = true;
                    LastError = null;

                    // Wait for a restart signal. This event will automatically reset.
                    // While we're restarting, it is possible for another restart to be
                    // signaled. That is fine - the restart will be processed immediately
                    // once we get to this line again. The important thing is that these
                    // restarts are only happening on a single thread.
                    WaitHandle.WaitAny(new WaitHandle[] 
                    {
                        cancellationToken.WaitHandle,
                        newInstance.RestartEvent,
                        _stopEvent
                    });

                    // Orphan the current host instance. We're stopping it, so it won't listen for any new functions
                    // it will finish any currently executing functions and then clean itself up.
                    // Spin around and create a new host instance.
                    Task taskIgnore = Orphan(newInstance);
                }
                catch (Exception ex)
                {
                    IsRunning = false;
                    LastError = ex;

                    // We need to keep the host running, so we catch and log any errors
                    // then restart the host
                    if (_traceWriter != null)
                    {
                        _traceWriter.Error("A ScriptHost error occurred", ex);
                    }

                    // If a ScriptHost instance was created before the exception was thrown
                    // Orphan and cleanup that instance.
                    if (newInstance != null)
                    {
                        Orphan(newInstance, forceStop: true)
                            .ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                {
                                    t.Exception.Handle(e => true);
                                }
                            });
                    }

                    // Wait for a short period of time before restarting to
                    // avoid cases where a host level config error might cause
                    // a rapid restart cycle
                    Task.Delay(5000).Wait();
                }
            }
            while (!_stopped && !cancellationToken.IsCancellationRequested);
        }

        private static void LogErrors(ScriptHost host)
        {
            if (host.FunctionErrors.Count > 0)
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "The following {0} functions are in error:", host.FunctionErrors.Count));
                foreach (var error in host.FunctionErrors)
                {
                    string functionErrors = string.Join(Environment.NewLine, error.Value);
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}: {1}", error.Key, functionErrors));
                }
                host.TraceWriter.Error(builder.ToString());
            }
        }

        /// <summary>
        /// Remove the <see cref="ScriptHost"/> instance from the live instances collection,
        /// allowing it to finish currently executing functions before stopping and disposing of it.
        /// </summary>
        /// <param name="instance">The <see cref="ScriptHost"/> instance to remove</param>
        /// <param name="forceStop">Forces the call to stop and dispose of the instance, even if it isn't present in the live instances collection.</param>
        /// <returns></returns>
        private async Task Orphan(ScriptHost instance, bool forceStop = false)
        {
            lock (_liveInstances)
            {
                bool removed = _liveInstances.Remove(instance);
                if (!forceStop && !removed)
                {
                    return; // somebody else is handling it
                }
            }

            try
            {
                // this thread now owns the instance
                await instance.StopAsync();
            }
            finally
            {
                instance.Dispose();
            }
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

        protected virtual void OnInitializeConfig(JobHostConfiguration config)
        {
        }

        protected virtual void OnHostStarted()
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                ScriptHost[] instances = GetLiveInstancesAndClear();
                foreach (var instance in instances)
                {
                    instance.Dispose();
                }

                _stopEvent.Dispose();

                _disposed = true;
            }
        }
    }
}
