// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class SingletonListener : IListener
    {
        private readonly SingletonAttribute _attribute;
        private readonly SingletonManager _singletonManager;
        private readonly SingletonConfiguration _singletonConfig;
        private readonly IListener _innerListener;
        private readonly TraceWriter _trace;
        private string _lockId;
        private object _lockHandle;
        private bool _isListening;

        public SingletonListener(MethodInfo method, SingletonAttribute attribute, SingletonManager singletonManager, IListener innerListener, TraceWriter trace)
        {
            _attribute = attribute;
            _singletonManager = singletonManager;
            _singletonConfig = _singletonManager.Config;
            _innerListener = innerListener;

            string boundScopeId = _singletonManager.GetBoundScopeId(_attribute.ScopeId);
            _lockId = singletonManager.FormatLockId(method, _attribute.Scope, boundScopeId);
            _lockId += ".Listener";

            _trace = trace;
        }

        // exposed for testing
        internal System.Timers.Timer LockTimer { get; set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // When recovery is enabled, we don't do retries on the individual lock attempts,
            // since retries are being done outside
            bool recoveryEnabled = _singletonConfig.ListenerLockRecoveryPollingInterval != TimeSpan.MaxValue;
            _lockHandle = await _singletonManager.TryLockAsync(_lockId, null, _attribute, cancellationToken, retry: !recoveryEnabled);

            if (_lockHandle == null)
            {
                _trace.Verbose(string.Format(CultureInfo.InvariantCulture, "Unable to acquire Singleton lock ({0}).", _lockId), source: TraceSource.Execution);

                // If we're unable to acquire the lock, it means another listener
                // has it so we return w/o starting our listener.
                //
                // However, we also start a periodic background "recovery" timer that will recheck
                // occasionally for the lock. This ensures that if the host that has the lock goes
                // down for whatever reason, others will have a chance to resume the work.
                if (recoveryEnabled)
                {
                    LockTimer = new System.Timers.Timer(_singletonConfig.ListenerLockRecoveryPollingInterval.TotalMilliseconds);
                    LockTimer.Elapsed += OnLockTimer;
                    LockTimer.Start();
                }
                return;
            }

            await _innerListener.StartAsync(cancellationToken);

            _isListening = true;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (LockTimer != null)
            {
                LockTimer.Stop();
            }

            await ReleaseLockAsync(cancellationToken);

            if (_isListening)
            {
                await _innerListener.StopAsync(cancellationToken);
                _isListening = false;
            }
        }

        public void Cancel()
        {
            if (LockTimer != null)
            {
                LockTimer.Stop();
            }

            _innerListener.Cancel();
        }

        public void Dispose()
        {
            if (LockTimer != null)
            {
                LockTimer.Dispose();
            }

            // When we Dispose, it's important that we release the lock if we
            // have it.
            ReleaseLockAsync().GetAwaiter().GetResult();

            _innerListener.Dispose();
        }

        private void OnLockTimer(object sender, ElapsedEventArgs e)
        {
            TryAcquireLock().GetAwaiter().GetResult();
        }

        internal async Task TryAcquireLock()
        {
            _lockHandle = await _singletonManager.TryLockAsync(_lockId, null, _attribute, CancellationToken.None, retry: false);

            if (_lockHandle != null)
            {
                if (LockTimer != null)
                {
                    LockTimer.Stop();
                    LockTimer.Dispose();
                    LockTimer = null;
                }

                await _innerListener.StartAsync(CancellationToken.None);

                _isListening = true;
            }
        }

        private async Task ReleaseLockAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_lockHandle != null)
            {
                await _singletonManager.ReleaseLockAsync(_lockHandle, cancellationToken);
                _lockHandle = null;
            }
        }
    }
}
