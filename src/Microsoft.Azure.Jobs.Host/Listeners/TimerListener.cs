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
