// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Listeners
{
    internal sealed class TimerListener : IListener
    {
        private readonly IntervalSeparationTimer _timer;

        private bool _disposed;

        public TimerListener(IntervalSeparationTimer timer)
        {
            _timer = timer;
        }

        public void Start()
        {
            ThrowIfDisposed();
            _timer.Start(executeFirst: false);
        }

        public void Stop()
        {
            ThrowIfDisposed();
            _timer.Stop();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _timer.Dispose();
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
