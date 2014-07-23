// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal sealed class Runner : IRunner
    {
        private readonly IntervalSeparationTimer _timer;
        private readonly CancellationTokenSource _hostCancellationTokenSource;
        private readonly WebJobsShutdownWatcher _watcher;
        private readonly IFunctionExecutor _executor;
        private readonly IListener _listener;

        private bool _disposed;
        private bool _stopped;

        public Runner(IntervalSeparationTimer timer, CancellationTokenSource hostCancellationTokenSource,
            WebJobsShutdownWatcher watcher, IFunctionExecutor executor, IListener listener)
        {
            _timer = timer;
            _hostCancellationTokenSource = hostCancellationTokenSource;
            _watcher = watcher;
            _executor = executor;
            _listener = listener;
        }

        public CancellationToken HostCancellationToken
        {
            get
            {
                ThrowIfDisposed();
                return _hostCancellationTokenSource.Token;
            }
        }

        public IFunctionExecutor Executor
        {
            get
            {
                ThrowIfDisposed();
                return _executor;
            }
        }

        public void Cancel()
        {
            _hostCancellationTokenSource.Cancel();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (_stopped)
            {
                throw new InvalidOperationException("The runner has already been stopped.");
            }

            _hostCancellationTokenSource.Cancel();
            await _listener.StopAsync(cancellationToken);
            _timer.Stop();
            _stopped = true;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Running callers might still be using the cancellation token.
                // Mark it canceled but don't dispose of the source while the callers are running.
                // Otherwise, callers would receive ObjectDisposedException when calling token.Register.
                // For now, rely on finalization to clean up _cancellationTokenSource's wait handle (if allocated).
                _hostCancellationTokenSource.Cancel();

                if (_watcher != null)
                {
                    _watcher.Dispose();
                }

                _timer.Dispose();
                _listener.Dispose();

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
