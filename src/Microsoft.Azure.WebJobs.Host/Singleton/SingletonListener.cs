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
            string lockId = SingletonManager.FormatLockId(_method, _attribute.Scope);
            lockId += ".Listener";

            // for listener locks, if the user hasn't explicitly set an override,
            // we want to default the timeout to something lower than the global default,
            // since we want startup to be relatively fast.
            if (_attribute.LockAcquisitionTimeout == null)
            {
                _attribute.LockAcquisitionTimeout = 15;
            }
 
            _lockHandle = await _singletonManager.TryLockAsync(lockId, null, _attribute, cancellationToken);

            if (_lockHandle == null)
            {
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
