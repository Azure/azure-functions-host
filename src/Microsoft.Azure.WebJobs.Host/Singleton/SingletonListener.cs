// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class SingletonListener : IListener
    {
        private MethodInfo _method;
        private SingletonAttribute _attribute;
        private SingletonManager _singletonManager;
        private IListener _innerListener;
        private object _lockHandle;
        private bool _isListening;

        public SingletonListener(MethodInfo method, SingletonAttribute attribute, SingletonManager singletonManager, IListener innerListener)
        {
            _method = method;
            _attribute = attribute;
            _singletonManager = singletonManager;
            _innerListener = innerListener;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            string boundScope = _singletonManager.GetBoundScope(_attribute.Scope);
            string lockId = SingletonManager.FormatLockId(_method, boundScope);
            lockId += ".Listener";

            // for listener locks, if the user hasn't explicitly set an override on the
            // attribute, we want to default the timeout to the lock period. We want to
            // stop as soon as possible (since we want startup to be relatively fast)
            // however we can't give up before waiting for a natural lease expiry.
            if (_attribute.LockAcquisitionTimeout == null)
            {
                _attribute.LockAcquisitionTimeout = (int)_singletonManager.Config.LockPeriod.TotalSeconds;
            }
 
            _lockHandle = await _singletonManager.TryLockAsync(lockId, null, _attribute, cancellationToken);

            if (_lockHandle == null)
            {
                // if we're unable to acquire the lock, it means another listener
                // has it so we return w/o starting our listener
                return;
            }

            await _innerListener.StartAsync(cancellationToken);

            _isListening = true;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_isListening)
            {
                return;
            }

            if (_lockHandle != null)
            {
                await _singletonManager.ReleaseLockAsync(_lockHandle, cancellationToken);
            }

            await _innerListener.StopAsync(cancellationToken);
        }

        public void Cancel()
        {
            _innerListener.Cancel();
        }

        public void Dispose()
        {
            _innerListener.Dispose();
        }
    }
}
