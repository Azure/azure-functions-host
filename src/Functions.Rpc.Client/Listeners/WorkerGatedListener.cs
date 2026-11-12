// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Runs a trigger listener only while a worker can serve its function group.
/// </summary>
/// <remarks>
/// The Host lifecycle (<see cref="StartAsync"/> and <see cref="StopAsync"/>) only registers this listener with the
/// <see cref="WorkerGatedListenerManager"/>, which starts and stops listener generations as workers come and go.
/// Extension listeners generally cannot restart after a stop, so each start after a stop creates a new generation from
/// the function's listener factory. Stopped generations are disposed after a delay so in-flight work that still refers
/// to them can complete.
/// </remarks>
internal sealed partial class WorkerGatedListener(
    IListener initialListener,
    IListenerFactory listenerFactory,
    string functionName,
    string functionGroup,
    bool startOnly,
    WorkerGatedListenerManager manager,
    TimeProvider timeProvider,
    ILogger logger,
    bool cancelDuringStart = false,
    BlobListenerQueueGate blobQueueGate = null) : IListener
{
    internal static readonly TimeSpan RetiredListenerDisposalDelay = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan FailedStartStopTimeout = TimeSpan.FromSeconds(30);
    private const string TimerNotStartedMessage = "The listener has not yet been started or has already been stopped.";

    private readonly IListener _initialListener = initialListener ?? throw new ArgumentNullException(nameof(initialListener));
    private readonly IListenerFactory _listenerFactory = listenerFactory ?? throw new ArgumentNullException(nameof(listenerFactory));
    private readonly WorkerGatedListenerManager _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Serializes Host lifecycle calls with manager-driven generation starts and stops.
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    // Signals pending starts when the Host stops or cancels.
    private readonly CancellationTokenSource _stopSource = new();
    private readonly CancellationTokenSource _disposeSource = new();
    private readonly Lock _retiredLock = new();
    private readonly List<IListener> _retired = [];
    private IListener _current;
    private IListener _failedStartListener;
    private Exception _failedStartCleanupFailure;
    private bool _initialUnused = true;
    private bool _initialDisposed;
    private bool _stopFailed;
    private bool _hostStarted;
    private bool _hostStopped;
    private int _disposed;

    internal string FunctionName { get; } = functionName ?? throw new ArgumentNullException(nameof(functionName));

    internal string FunctionGroup { get; } = functionGroup ?? throw new ArgumentNullException(nameof(functionGroup));

    internal bool IsStartOnly => startOnly;

    internal BlobListenerQueueGate BlobQueueGate { get; } = blobQueueGate;

    internal bool IsListening => Interlocked.CompareExchange(ref _current, null, null) is not null;

    private bool IsDisposed => Interlocked.CompareExchange(ref _disposed, 0, 0) != 0;

    /// <summary>
    /// Registers with the manager; a listener generation starts only when a worker can serve the function group.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using (await _lifecycleLock.LockAsync(cancellationToken))
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_hostStarted || _hostStopped)
            {
                throw new InvalidOperationException("The listener has already been started.");
            }

            _hostStarted = true;
        }

        _manager.Register(this);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _manager.Unregister(this);
        CancelPendingStart();

        using (await _lifecycleLock.LockAsync(cancellationToken))
        {
            _hostStopped = true;
            await StopCurrentAsync(cancellationToken, throwOnFailure: true);
            if (_failedStartCleanupFailure is { } failure)
            {
                throw new InvalidOperationException($"Listener for function '{FunctionName}' could not be safely stopped after a failed start.", failure);
            }
        }
    }

    public void Cancel()
    {
        CancelPendingStart();
        Interlocked.CompareExchange(ref _current, null, null)?.Cancel();
        Interlocked.CompareExchange(ref _failedStartListener, null, null)?.Cancel();
    }

    /// <summary>
    /// Starts a listener generation when the Host has started this listener and none is running.
    /// </summary>
    /// <returns><see langword="false"/> when the listener could not be safely started and should be retried.</returns>
    internal async Task<bool> StartListeningAsync(CancellationToken cancellationToken)
    {
        if (IsDisposed)
        {
            return true;
        }

        using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopSource.Token);
        CancellationToken token = linkedSource.Token;
        SemaphoreLock lifecycleLock;
        try
        {
            lifecycleLock = await _lifecycleLock.LockAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return _stopSource.IsCancellationRequested;
        }

        using (lifecycleLock)
        {
            if (!_hostStarted || _hostStopped || IsDisposed)
            {
                return true;
            }

            if (_failedStartCleanupFailure is not null)
            {
                return false;
            }

            if (_stopFailed && !await StopCurrentAsync(token))
            {
                return false;
            }

            if (_current is not null)
            {
                return true;
            }

            IListener listener = null;
            bool startRequested = false;
            bool cancelRequested = false;
            bool cancellationFailed = false;
            try
            {
                token.ThrowIfCancellationRequested();
                if (_initialUnused)
                {
                    _initialUnused = false;
                    listener = _initialListener;
                }
                else
                {
                    listener = await _listenerFactory.CreateAsync(token);
                }

                token.ThrowIfCancellationRequested();

                Log.StartingListener(_logger, FunctionName, FunctionGroup);
                token.ThrowIfCancellationRequested();

                startRequested = true;
                if (cancelDuringStart && !startOnly)
                {
                    Task cancelTask = null;
                    try
                    {
                        // Timer can ignore its startup token; do not run its Cancel callback on the reconciliation loop.
                        await using (token.Register(() => Interlocked.Exchange(ref cancelTask, Task.Run(listener.Cancel))))
                        {
                            token.ThrowIfCancellationRequested();
                            // SingletonListener also uses its start token to release its lease after a failed start.
                            await listener.StartAsync(_stopSource.Token);
                        }
                    }
                    finally
                    {
                        if (cancelTask is not null)
                        {
                            cancelRequested = true;
                            try
                            {
                                await cancelTask;
                            }
                            catch (Exception exception)
                            {
                                cancellationFailed = true;
                                Log.CancellationCallbackFailed(_logger, exception, FunctionName);
                            }
                        }
                    }

                    token.ThrowIfCancellationRequested();
                }
                else
                {
                    // Once a shared Blob start begins, worker loss must not interrupt its non-restartable listener.
                    await listener.StartAsync(startOnly ? _stopSource.Token : token);
                    // A successfully started shared Blob listener cannot be stopped and restarted on worker loss.
                    if (!startOnly || _stopSource.IsCancellationRequested)
                    {
                        token.ThrowIfCancellationRequested();
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                await RetireFailedStartAsync(listener, startRequested, cancelRequested, cancellationFailed);
                return false;
            }
            catch (Exception exception)
            {
                Log.ListenerStartFailed(_logger, exception, FunctionName, FunctionGroup);
                await RetireFailedStartAsync(listener, startRequested, cancelRequested, cancellationFailed);
                return false;
            }

            Interlocked.Exchange(ref _current, listener);

            // Dispose owns the generation once this lifecycle operation releases the lock.
            if (IsDisposed)
            {
                return true;
            }

            Log.StartedListener(_logger, FunctionName, FunctionGroup);
            return true;
        }
    }

    /// <summary>
    /// Stops the running listener generation, unless this listener cannot be restarted after a stop.
    /// </summary>
    /// <returns><see langword="false"/> when the generation failed to stop and should be retried.</returns>
    internal async Task<bool> StopListeningAsync(CancellationToken cancellationToken)
    {
        using (await _lifecycleLock.LockAsync(cancellationToken))
        {
            if (_current is null)
            {
                return true;
            }

            if (startOnly)
            {
                return true;
            }

            Log.StoppingListener(_logger, FunctionName, FunctionGroup);
            return await StopCurrentAsync(cancellationToken);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _manager.Unregister(this);
        CancelPendingStart();
        _disposeSource.Cancel();

        List<IListener> listeners = [];
        lock (_retiredLock)
        {
            listeners.AddRange(_retired);
            _retired.Clear();
        }

        ValueTask<SemaphoreLock> lifecycleLock = _lifecycleLock.LockAsync();
        Exception retiredFailure = null;
        try
        {
            DisposeListeners(listeners);
        }
        catch (Exception exception)
        {
            retiredFailure = exception;
        }

        Task cleanup = DisposeCurrentAndInitialAsync(lifecycleLock);
        if (cleanup.IsCompleted)
        {
            try
            {
                cleanup.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                if (retiredFailure is not null)
                {
                    throw new AggregateException(retiredFailure, exception);
                }

                throw;
            }
        }
        else
        {
            // A synchronous Dispose cannot wait for an asynchronous listener operation.
            _ = ObserveDisposeAsync(cleanup);
        }

        if (retiredFailure is not null)
        {
            ExceptionDispatchInfo.Capture(retiredFailure).Throw();
        }
    }

    private async Task DisposeCurrentAndInitialAsync(ValueTask<SemaphoreLock> lifecycleLock)
    {
        List<Exception> failures = null;
        using (await lifecycleLock)
        {
            List<IListener> listeners = [];
            IListener current = _current;
            if (current is not null)
            {
                try
                {
                    await StopCurrentAsync(CancellationToken.None, throwOnFailure: true, retireOnSuccess: false);
                    if (!ReferenceEquals(current, _initialListener))
                    {
                        listeners.Add(current);
                    }
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            IListener failedStartListener = _failedStartListener;
            if (failedStartListener is not null)
            {
                try
                {
                    await failedStartListener.StopAsync(CancellationToken.None);
                    Interlocked.Exchange(ref _failedStartListener, null);
                    if (!ReferenceEquals(failedStartListener, _initialListener))
                    {
                        listeners.Add(failedStartListener);
                    }
                }
                catch (Exception exception)
                {
                    Log.ListenerStopFailed(_logger, exception, FunctionName, FunctionGroup);
                    (failures ??= []).Add(exception);
                }
            }

            if (_current is not null || _failedStartListener is not null)
            {
                Log.ListenerDisposeBlocked(_logger, FunctionName);
            }

            if (!_initialDisposed && !ReferenceEquals(_initialListener, _current) && !ReferenceEquals(_initialListener, _failedStartListener))
            {
                _initialDisposed = true;
                listeners.Add(_initialListener);
            }

            try
            {
                DisposeListeners(listeners);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        ThrowDisposalFailures(failures);
    }

    private async Task ObserveDisposeAsync(Task cleanup)
    {
        try
        {
            await cleanup;
        }
        catch (Exception exception)
        {
            Log.ListenerDisposeFailed(_logger, exception, FunctionName);
        }
    }

    private static void DisposeListeners(List<IListener> listeners)
    {
        // Release every generation even when one fails, then surface the failures together.
        List<Exception> exceptions = null;
        foreach (IListener listener in listeners)
        {
            try
            {
                listener.Dispose();
            }
            catch (Exception exception)
            {
                (exceptions ??= []).Add(exception);
            }
        }

        ThrowDisposalFailures(exceptions);
    }

    private static void ThrowDisposalFailures(List<Exception> exceptions)
    {
        if (exceptions?.Count == 1)
        {
            ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
        }

        if (exceptions is not null)
        {
            throw new AggregateException(exceptions);
        }
    }

    private void CancelPendingStart()
    {
        try
        {
            _stopSource.Cancel();
        }
        catch (AggregateException exception)
        {
            Log.CancellationCallbackFailed(_logger, exception, FunctionName);
        }
    }

    // Requires the lifecycle lock.
    private async Task<bool> StopCurrentAsync(CancellationToken cancellationToken, bool throwOnFailure = false, bool retireOnSuccess = true)
    {
        IListener listener = _current;
        if (listener is null)
        {
            return true;
        }

        try
        {
            await listener.StopAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _stopFailed = true;
            Log.ListenerStopFailed(_logger, exception, FunctionName, FunctionGroup);
            if (throwOnFailure)
            {
                throw;
            }

            return false;
        }

        _stopFailed = false;
        Interlocked.Exchange(ref _current, null);
        if (retireOnSuccess)
        {
            Retire(listener);
        }

        Log.StoppedListener(_logger, FunctionName, FunctionGroup);
        return true;
    }

    private async Task RetireFailedStartAsync(IListener listener, bool startRequested, bool cancelRequested, bool cancellationFailed)
    {
        if (!startRequested && ReferenceEquals(listener, _initialListener))
        {
            _initialUnused = true;
            return;
        }

        if (startRequested && listener is not null)
        {
            try
            {
                if (cancelDuringStart)
                {
                    // Timer may drain past the timeout; the singleton still needs an uncanceled token to release its lease.
                    await listener.StopAsync(CancellationToken.None);
                }
                else
                {
                    using CancellationTokenSource timeoutSource = new(FailedStartStopTimeout, _timeProvider);
                    await listener.StopAsync(timeoutSource.Token);
                }
            }
            catch (Exception stopException)
            {
                // This SDK error identifies the Timer that was canceled before it created a timer.
                if (cancelRequested && !cancellationFailed && stopException.GetType() == typeof(InvalidOperationException)
                    && string.Equals(stopException.Message, TimerNotStartedMessage, StringComparison.Ordinal))
                {
                    // A canceled Timer start may never create its timer, so Stop can fail before releasing the singleton lease.
                    try
                    {
                        if (ReferenceEquals(listener, _initialListener))
                        {
                            _initialDisposed = true;
                        }

                        listener.Dispose();
                    }
                    catch (Exception disposeException)
                    {
                        stopException = new AggregateException(stopException, disposeException);
                        _failedStartCleanupFailure = stopException;
                        Log.ListenerRestartBlocked(_logger, FunctionName);
                    }

                    Log.ListenerStopFailed(_logger, stopException, FunctionName, FunctionGroup);
                    return;
                }

                // A failed cancellation or an unexpected stop failure may leave work in flight.
                _failedStartCleanupFailure = stopException;
                Interlocked.Exchange(ref _failedStartListener, listener);
                Log.ListenerRestartBlocked(_logger, FunctionName);
                Log.ListenerStopFailed(_logger, stopException, FunctionName, FunctionGroup);
                return;
            }
        }

        Retire(listener);
    }

    // The initial listener is owned until this listener is disposed; later generations are disposed after a grace period.
    private void Retire(IListener listener)
    {
        if (listener is null || ReferenceEquals(listener, _initialListener))
        {
            return;
        }

        lock (_retiredLock)
        {
            if (!IsDisposed)
            {
                _retired.Add(listener);
                _ = DisposeRetiredAsync(listener);
                return;
            }
        }

        DisposeListener(listener);
    }

    private async Task DisposeRetiredAsync(IListener listener)
    {
        try
        {
            await Task.Delay(RetiredListenerDisposalDelay, _timeProvider, _disposeSource.Token);
        }
        catch (OperationCanceledException)
        {
            // Dispose owns every listener that is still retired.
            return;
        }

        lock (_retiredLock)
        {
            if (!_retired.Remove(listener))
            {
                return;
            }
        }

        DisposeListener(listener);
    }

    private void DisposeListener(IListener listener)
    {
        try
        {
            listener.Dispose();
        }
        catch (Exception exception)
        {
            Log.ListenerDisposeFailed(_logger, exception, FunctionName);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information,
            "Starting listener for function '{FunctionName}' because a worker for group '{FunctionGroup}' is available.")]
        public static partial void StartingListener(ILogger logger, string functionName, string functionGroup);

        [LoggerMessage(1, LogLevel.Information, "Started listener for function '{FunctionName}' in group '{FunctionGroup}'.")]
        public static partial void StartedListener(ILogger logger, string functionName, string functionGroup);

        [LoggerMessage(2, LogLevel.Error,
            "Listener for function '{FunctionName}' in group '{FunctionGroup}' failed to start.")]
        public static partial void ListenerStartFailed(ILogger logger, Exception exception, string functionName, string functionGroup);

        [LoggerMessage(3, LogLevel.Information,
            "Stopping listener for function '{FunctionName}' because no worker for group '{FunctionGroup}' is available.")]
        public static partial void StoppingListener(ILogger logger, string functionName, string functionGroup);

        [LoggerMessage(4, LogLevel.Information, "Stopped listener for function '{FunctionName}' in group '{FunctionGroup}'.")]
        public static partial void StoppedListener(ILogger logger, string functionName, string functionGroup);

        [LoggerMessage(5, LogLevel.Error, "Listener for function '{FunctionName}' in group '{FunctionGroup}' failed to stop.")]
        public static partial void ListenerStopFailed(ILogger logger, Exception exception, string functionName, string functionGroup);

        [LoggerMessage(7, LogLevel.Error, "Failed to dispose a listener for function '{FunctionName}'.")]
        public static partial void ListenerDisposeFailed(ILogger logger, Exception exception, string functionName);

        [LoggerMessage(8, LogLevel.Warning, "A cancellation callback failed while canceling the listener for function '{FunctionName}'.")]
        public static partial void CancellationCallbackFailed(ILogger logger, Exception exception, string functionName);

        [LoggerMessage(9, LogLevel.Error,
            "The listener for function '{FunctionName}' cannot restart because cleanup failed. The host must restart before the listener can run again.")]
        public static partial void ListenerRestartBlocked(ILogger logger, string functionName);

        [LoggerMessage(10, LogLevel.Error,
            "The listener for function '{FunctionName}' was not disposed because it could not be stopped safely. The host process may need to restart.")]
        public static partial void ListenerDisposeBlocked(ILogger logger, string functionName);
    }
}
