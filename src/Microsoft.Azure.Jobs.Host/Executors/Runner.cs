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
        private readonly IDisposable _disposable;
        private readonly IntervalSeparationTimer _timer;
        private readonly IFunctionExecutor _executor;
        private readonly IListener _listener;
        private readonly CancellationToken _cancellationToken;

        private bool _disposed;
        private bool _stopped;

        public Runner(IDisposable disposable, IntervalSeparationTimer timer, IFunctionExecutor executor,
            IListener listener, CancellationToken cancellationToken)
        {
            _disposable = disposable;
            _timer = timer;
            _executor = executor;
            _listener = listener;
            _cancellationToken = cancellationToken;
        }

        public CancellationToken CancellationToken
        {
            get
            {
                ThrowIfDisposed();
                return _cancellationToken;
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
                // _disposable is responsible for handling _timer and _listener.
                _disposable.Dispose();
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
