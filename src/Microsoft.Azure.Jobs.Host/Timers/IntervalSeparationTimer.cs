// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.Timers
{
    /// <summary>
    /// Represents a timer that keeps a heartbeat running at a specified interval using a separate thread.
    /// </summary>
    internal sealed class IntervalSeparationTimer : IDisposable
    {
        private static readonly TimeSpan infiniteTimeout = TimeSpan.FromMilliseconds(-1);

        private readonly IIntervalSeparationCommand _command;
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly CancellationTokenSource _cancellationTokenSource;

        private bool _started;
        private bool _stopped;
        private Task _run;
        private bool _disposed;

        public IntervalSeparationTimer(IIntervalSeparationCommand command)
            : this(command, BackgroundExceptionDispatcher.Instance)
        {
        }

        public IntervalSeparationTimer(IIntervalSeparationCommand command,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher)
        {
            if (command == null)
            {
                throw new ArgumentNullException("command");
            }

            if (backgroundExceptionDispatcher == null)
            {
                throw new ArgumentNullException("backgroundExceptionDispatcher");
            }

            _command = command;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void Start()
        {
            ThrowIfDisposed();

            if (_started)
            {
                throw new InvalidOperationException("The timer has already been started; it cannot be restarted.");
            }

            _run = RunAsync(_cancellationTokenSource.Token);
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

            _cancellationTokenSource.Cancel();

            // Wait for all pending command tasks to complete before returning.
            _run.GetAwaiter().GetResult();

            _stopped = true;
        }

        public void Cancel()
        {
            ThrowIfDisposed();
            _cancellationTokenSource.Cancel();
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

                _disposed = true;
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Allow Start to return immediately without waiting for the first command to execute.
                await Task.Yield();

                List<Task> runningCommands = new List<Task>();

                // Schedule another execution until stopped.
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_command.SeparationInterval, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // When Stop fires, don't make it wait for _command.SeparationInterval before it can return.
                        break;
                    }

                    Task commandTask = _command.ExecuteAsync(cancellationToken);
                    Task continuationTask = commandTask.ContinueWith((t) =>
                    {
                        if (t.Status == TaskStatus.Faulted)
                        {
                            ExceptionDispatchInfo exceptionInfo;

                            try
                            {
                                t.GetAwaiter().GetResult();
                                throw new InvalidOperationException(
                                    "Getting the result of a faulted task should throw an exception.");
                            }
                            catch (Exception exception)
                            {
                                exceptionInfo = ExceptionDispatchInfo.Capture(exception);
                            }

                            _backgroundExceptionDispatcher.Throw(exceptionInfo);
                        }

                        // Just allow the continuation task to run to completion if t.Status is Canceled or RanToCompletion.
                    }, TaskContinuationOptions.ExecuteSynchronously);

                    runningCommands.Add(continuationTask);
                }

                await Task.WhenAll(runningCommands);
            }
            catch (Exception exception)
            {
                // Immediately report any unhandled exception from this background task.
                // (Don't capture the exception as a fault of this Task; that would delay any exception reporting until
                // Stop is called, which might never happen.)
                _backgroundExceptionDispatcher.Throw(ExceptionDispatchInfo.Capture(exception));
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
