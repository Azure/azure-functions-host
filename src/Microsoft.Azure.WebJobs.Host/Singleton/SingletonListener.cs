// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class SingletonListener : IListener
    {
        private readonly MethodInfo _method;
        private readonly SingletonAttribute _attribute;
        private readonly SingletonManager _singletonManager;
        private readonly IListener _innerListener;
        private string _lockId;
        private object _lockHandle;
        private bool _isListening;

        public SingletonListener(MethodInfo method, SingletonAttribute attribute, SingletonManager singletonManager, IListener innerListener)
        {
            _method = method;
            _attribute = attribute;
            _singletonManager = singletonManager;
            _innerListener = innerListener;

            string boundScope = _singletonManager.GetBoundScope(_attribute.Scope);
            _lockId = SingletonManager.FormatLockId(_method, boundScope);
            _lockId += ".Listener";
        }

        // exposed for testing
        internal System.Timers.Timer LockTimer { get; set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // for listener locks, if the user hasn't explicitly set an override on the
            // attribute, we want to default the timeout to the lock period. We want to
            // stop as soon as possible (since we want startup to be relatively fast)
            // however we don't want to give up before waiting for a natural lease expiry.
            // If we miss the lock, the recovery timer will periodically reattempt to acquire.
            if (_attribute.LockAcquisitionTimeout == null)
            {
                _attribute.LockAcquisitionTimeout = (int)_singletonManager.Config.LockPeriod.TotalSeconds;
            }
 
            _lockHandle = await _singletonManager.TryLockAsync(_lockId, null, _attribute, cancellationToken);

            if (_lockHandle == null)
            {
                // If we're unable to acquire the lock, it means another listener
                // has it so we return w/o starting our listener
                //
                // However, we also start a periodic background "recovery" timer that will recheck
                // occasionally for the lock. This ensures that if the host that has the lock goes
                // down for whatever reason, others will have a chance to resume the work.
                TimeSpan recoveryPollingInterval = _singletonManager.Config.ListenerLockRecoveryPollingInterval;
                if (recoveryPollingInterval != Timeout.InfiniteTimeSpan)
                {
                    LockTimer = new System.Timers.Timer(recoveryPollingInterval.TotalMilliseconds);
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

            if (_lockHandle != null)
            {
                await _singletonManager.ReleaseLockAsync(_lockHandle, cancellationToken);
            }

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

            _innerListener.Dispose();
        }

        private void OnLockTimer(object sender, ElapsedEventArgs e)
        {
            TryAcquireLock().Wait();
        }

        internal async Task TryAcquireLock()
        {
            _lockHandle = await _singletonManager.TryLockAsync(_lockId, null, _attribute, CancellationToken.None);

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
    }
}
