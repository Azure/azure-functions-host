using System;
using System.Diagnostics.Contracts;
using System.Threading;

namespace Microsoft.WindowsAzure.Jobs
{
    /// <summary>Represents a timer that keeps a heartbeat running at a specified interval using a separate thread.</summary>
    internal sealed class HeartbeatTimer : IDisposable
    {
        private readonly IHeartbeat _heartbeat;
        private readonly int _frequencyInMilliseconds;
        private readonly Thread _thread;
        private readonly EventWaitHandle _threadStopEvent;

        private bool _started;
        private bool _stopped;
        private bool _disposed;

        public HeartbeatTimer(IHeartbeat heartbeat, int frequencyInMilliseconds)
        {
            if (heartbeat == null)
            {
                throw new ArgumentNullException("heartbeat");
            }

            if (frequencyInMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException("frequencyInMilliseconds");
            }

            _heartbeat = heartbeat;
            _frequencyInMilliseconds = frequencyInMilliseconds;

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

        public void Start()
        {
            ThrowIfDisposed();

            if (_started)
            {
                throw new InvalidOperationException();
            }

            // Do an initial heartbeat without waiting for the thread to start.
            _heartbeat.Beat();

            _thread.Start();
            _started = true;
        }

        public void Stop()
        {
            ThrowIfDisposed();

            if (!_started || _stopped)
            {
                throw new InvalidOperationException();
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
            // Keep beating until stopped.
            while (!_threadStopEvent.WaitOne(_frequencyInMilliseconds))
            {
                _heartbeat.Beat();
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
