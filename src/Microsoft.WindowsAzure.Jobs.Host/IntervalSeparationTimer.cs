using System;
using System.Diagnostics.Contracts;
using System.Threading;

namespace Microsoft.WindowsAzure.Jobs
{
    /// <summary>Represents a timer that keeps a heartbeat running at a specified interval using a separate thread.</summary>
    internal sealed class IntervalSeparationTimer : IDisposable
    {
        private readonly IIntervalSeparationCommand _command;
        private readonly Thread _thread;
        private readonly EventWaitHandle _threadStopEvent;

        private bool _started;
        private bool _stopped;
        private bool _disposed;

        public IntervalSeparationTimer(IIntervalSeparationCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException("command");
            }

            _command = command;

            _thread = new Thread(RunThread);
            _threadStopEvent = new ManualResetEvent(initialState: false);
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

                _threadStopEvent.Dispose();
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

            _thread.Start();
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

            // Signal the thread to complete.
            bool succeeded = _threadStopEvent.Set();
            // EventWaitHandle.Set can never return false (see implementation).
            Contract.Assert(succeeded);

            // Let the thread complete.
            _thread.Join();

            _stopped = true;
        }

        private void RunThread()
        {
            // Keep executing until stopped.
            while (!_threadStopEvent.WaitOne(GetNextSeparationInterval()))
            {
                _command.Execute();
            }
        }

        private int GetNextSeparationInterval()
        {
            double valueInMillseconds = _command.SeparationInterval.TotalMilliseconds;

            if (valueInMillseconds > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "IIntervalSeparationCommand.SeparationCommand must not be longer than Int32.MaxValue total milliseconds.");
            }

            return (int)valueInMillseconds;
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
