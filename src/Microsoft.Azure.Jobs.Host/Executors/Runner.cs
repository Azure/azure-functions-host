// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal sealed class Runner : IRunner
    {
        private readonly IntervalSeparationTimer _timer;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly WebJobsShutdownWatcher _watcher;
        private readonly IFunctionExecutor _executor;
        private readonly IListener _listener;

        private bool _disposed;
        private bool _stopped;

        public Runner(IntervalSeparationTimer timer, CancellationTokenSource cancellationTokenSource,
            WebJobsShutdownWatcher watcher, IFunctionExecutor executor, IListener listener)
        {
            _timer = timer;
            _cancellationTokenSource = cancellationTokenSource;
            _watcher = watcher;
            _executor = executor;
            _listener = listener;
        }

        public CancellationToken CancellationToken
        {
            get
            {
                ThrowIfDisposed();
                return _cancellationTokenSource.Token;
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

        public void Stop()
        {
            ThrowIfDisposed();

            if (_stopped)
            {
                throw new InvalidOperationException("The runner has already been stopped.");
            }

            _listener.Stop();
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
                _cancellationTokenSource.Cancel();

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
