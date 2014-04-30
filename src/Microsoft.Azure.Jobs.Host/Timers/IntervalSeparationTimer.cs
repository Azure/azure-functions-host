using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Threading;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Represents a timer that keeps a heartbeat running at a specified interval using a separate thread.</summary>
    internal sealed class IntervalSeparationTimer : IDisposable
    {
        private static readonly TimeSpan infiniteTimeout = TimeSpan.FromMilliseconds(-1);

        private readonly IIntervalSeparationCommand _command;
        private readonly Timer _timer;

        private bool _started;
        private volatile bool _stopping;
        private bool _stopped;
        private bool _disposed;

        public IntervalSeparationTimer(IIntervalSeparationCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException("command");
            }

            _command = command;
            _timer = new Timer(RunTimer);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_started && !_stopped)
                {
                    // Don't leave a runaway thread when Dispose is called.
                    // That means we need to signal the thread to stop and wait for the thread to finish (before we
                    // dispose of the event that the thread is waiting on).
                    Stop();
                }

                _disposed = true;
            }
        }

        public void Start(bool executeFirst)
        {
            ThrowIfDisposed();

            if (_started)
            {
                throw new InvalidOperationException("The timer has already been started; it cannot be restarted.");
            }

            if (executeFirst)
            {
                // Do an initial execution without waiting for the separation interval.
                _command.Execute();
            }

            bool changed = _timer.Change(_command.SeparationInterval, infiniteTimeout);
            Debug.Assert(changed);
            _started = true;
        }

        public void Stop()
        {
            ThrowIfDisposed();

            if (!_started)
            {
                throw new InvalidOperationException("The timer has not yet been started.");
            }

            if (_stopped)
            {
                throw new InvalidOperationException("The timer has already been stopped.");
            }

            _stopping = true;

            using (WaitHandle wait = new ManualResetEvent(initialState: false))
            {
                _timer.Dispose(wait);
                // Wait for all timer callbacks to complete before returning.
                bool completed = wait.WaitOne();
                Debug.Assert(completed);
            }

            _stopped = true;
        }

        private void RunTimer(object state)
        {
            _command.Execute();

            // Schedule another execution until stopped.
            // Don't call _timer.Change on one thread after another thread may have called _timer.Dispose.
            // Otherwise, this thread can get an ObjectDisposedException.
            if (!_stopping)
            {
                _timer.Change(_command.SeparationInterval, infiniteTimeout);
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
