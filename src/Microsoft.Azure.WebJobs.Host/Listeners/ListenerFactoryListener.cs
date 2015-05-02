// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class ListenerFactoryListener : IListener
    {
        private readonly IListenerFactory _factory;
        private readonly IFunctionExecutor _executor;
        private readonly CancellationTokenSource _cancellationSource;

        private IListener _listener;
        private CancellationTokenRegistration _cancellationRegistration;
        private bool _disposed;

        public ListenerFactoryListener(IListenerFactory factory, IFunctionExecutor executor)
        {
            _factory = factory;
            _executor = executor;
            _cancellationSource = new CancellationTokenSource();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (_listener != null)
            {
                throw new InvalidOperationException("The listener has already been started.");
            }

            return StartAsyncCore(cancellationToken);
        }

        private async Task StartAsyncCore(CancellationToken cancellationToken)
        {
            ListenerFactoryContext context = new ListenerFactoryContext(cancellationToken);
            _listener = await _factory.CreateAsync(context);
            _cancellationRegistration = _cancellationSource.Token.Register(_listener.Cancel);
            await _listener.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (_listener == null)
            {
                throw new InvalidOperationException("The listener has not been started.");
            }

            return _listener.StopAsync(cancellationToken);
        }

        public void Cancel()
        {
            ThrowIfDisposed();
            _cancellationSource.Cancel();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationRegistration.Dispose();

                // StartAsync might still be using this cancellation token.
                // Mark it canceled but don't dispose of the source while the callers are running.
                // Otherwise, callers would receive ObjectDisposedException when calling token.Register.
                // For now, rely on finalization to clean up _shutdownTokenSource's wait handle (if allocated).
                _cancellationSource.Cancel();

                if (_listener != null)
                {
                    _listener.Dispose();
                }

                _disposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }
    }
}
