// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Owns initialized client-backed worker channels and terminal cleanup.
/// </summary>
/// <remarks>
/// Admission and replay decisions are atomic. Different workers can link concurrently.
/// The registry publishes a channel only after its WorkerInit handshake succeeds and retains terminal identities.
/// </remarks>
internal sealed partial class WorkerChannelRegistry : IWorkerChannelRegistry
{
    private readonly IRpcClientWorkerChannelFactory _channelFactory;
    private readonly Lock _disposeLock = new();
    private readonly IDuplexChannelFactory<StreamingMessage> _duplexChannelFactory;
    private readonly ILogger<WorkerChannelRegistry> _logger;
    private readonly Dictionary<string, WorkerSlot> _linkAttempts = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _monitorTasks = [];
    private readonly CancellationTokenSource _shutdownSource = new();
    private readonly Dictionary<string, WorkerSlot> _slots = new(StringComparer.Ordinal);
    private readonly Lock _stateLock = new();
    private Task _disposeTask;
    private bool _disposed;
    private TaskCompletionSource _initializedChannelAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WorkerChannelRegistry(IDuplexChannelFactory<StreamingMessage> duplexChannelFactory,
        IRpcClientWorkerChannelFactory channelFactory, ILogger<WorkerChannelRegistry> logger)
    {
        _duplexChannelFactory = duplexChannelFactory ?? throw new ArgumentNullException(nameof(duplexChannelFactory));
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<WorkerChannel> LinkAsync(string workerId, Uri grpcEndpoint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        RpcClientFactory.ValidateEndpoint(grpcEndpoint);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource start;
        Task<WorkerChannel> link;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_linkAttempts.TryGetValue(workerId, out WorkerSlot existing))
            {
                if (!string.Equals(existing.Endpoint.AbsoluteUri, grpcEndpoint.AbsoluteUri, StringComparison.Ordinal))
                {
                    throw new WorkerLinkException(WorkerLinkFailureReason.Conflict,
                        "The worker is already associated with a different gRPC endpoint.");
                }

                if (existing.Terminal || existing.Channel?.Completion.IsCompleted == true)
                {
                    throw new WorkerLinkException(WorkerLinkFailureReason.WorkerTerminated,
                        "The worker's initialized channel has terminated.");
                }

                Task<WorkerChannel> replay = existing.LinkTask.WaitAsync(cancellationToken);

                return replay;
            }

            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            WorkerSlot slot = new() { Endpoint = grpcEndpoint };
            _slots.Add(workerId, slot);
            _linkAttempts.Add(workerId, slot);
            link = LinkCoreAsync(workerId, grpcEndpoint, slot, start.Task, cancellationToken);
            slot.LinkTask = link;
        }

        // Publish the shared attempt before starting I/O so a retry cannot start a second connection.
        start.SetResult();

        return link;
    }

    private async Task<WorkerChannel> LinkCoreAsync(string workerId, Uri grpcEndpoint, WorkerSlot slot,
        Task start, CancellationToken cancellationToken)
    {
        await start;
        using SemaphoreLock gate = new(slot.Gate);
        bool initialized = false;
        try
        {
            using CancellationTokenSource operationSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownSource.Token);
            operationSource.Token.ThrowIfCancellationRequested();

            DuplexChannel<StreamingMessage> ownedChannel = null;
            RpcClientWorkerChannel candidate = null;

            try
            {
                ownedChannel = await _duplexChannelFactory.ConnectAsync(grpcEndpoint, operationSource.Token);
                operationSource.Token.ThrowIfCancellationRequested();

                candidate = _channelFactory.Create(workerId, ownedChannel)
                    ?? throw new InvalidOperationException("The client worker channel factory returned no channel.");
                ownedChannel = null;

                if (!string.Equals(candidate.Id, workerId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"The client worker channel factory returned worker '{candidate.Id}' for requested worker '{workerId}'.");
                }

                try
                {
                    await candidate.StartAsync(operationSource.Token);
                }
                catch (InvalidOperationException exception) when (candidate.Completion.IsCompleted)
                {
                    throw new IOException("The worker channel closed before initialization completed.", exception);
                }

                operationSource.Token.ThrowIfCancellationRequested();
                RegisterInitializedChannel(workerId, slot, candidate);

                WorkerChannel linkedChannel = candidate;
                candidate = null;
                initialized = true;

                return linkedChannel;
            }
            catch (Exception exception)
            {
                Exception failure = exception is OperationCanceledException && !cancellationToken.IsCancellationRequested &&
                    _shutdownSource.IsCancellationRequested
                    ? new ObjectDisposedException(GetType().FullName)
                    : exception;
                failure = await candidate.DisposeAndCaptureExceptionAsync(failure);
                failure = await ownedChannel.DisposeAndCaptureExceptionAsync(failure);
                ExceptionDispatchInfo.Capture(failure).Throw();
                throw;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _shutdownSource.IsCancellationRequested)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
        finally
        {
            RemoveEmptySlot(workerId, slot);
            if (!initialized)
            {
                lock (_stateLock)
                {
                    _linkAttempts.Remove(workerId);
                }
            }
        }
    }

    public async Task<bool> UnlinkAsync(string workerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        cancellationToken.ThrowIfCancellationRequested();

        WorkerSlot slot;
        lock (_stateLock)
        {
            if (_disposed || !_slots.TryGetValue(workerId, out slot))
            {
                return false;
            }
        }

        try
        {
            using CancellationTokenSource operationSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownSource.Token);

            SemaphoreLock gate = await slot.Gate.LockAsync(operationSource.Token);
            try
            {
                using (gate)
                {
                    operationSource.Token.ThrowIfCancellationRequested();

                    RpcClientWorkerChannel channel;
                    lock (_stateLock)
                    {
                        if (_disposed)
                        {
                            return false;
                        }

                        channel = DetachChannelLocked(slot, expectedChannel: null);
                    }

                    if (channel is null)
                    {
                        return false;
                    }

                    await channel.DisposeAsync();
                    return true;
                }
            }
            finally
            {
                RemoveEmptySlot(workerId, slot);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _shutdownSource.IsCancellationRequested)
        {
            return false;
        }
    }

    public bool TryGetInitializedChannel(string workerId, out WorkerChannel channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        lock (_stateLock)
        {
            if (!_disposed && _slots.TryGetValue(workerId, out WorkerSlot slot) && slot.Channel is not null)
            {
                channel = slot.Channel;
                return true;
            }
        }

        channel = null;
        return false;
    }

    public IReadOnlyList<WorkerChannel> GetInitializedChannels()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return [];
            }

            return [.. _slots.Values.Where(slot => slot.Channel is not null).Select(slot => (WorkerChannel)slot.Channel)];
        }
    }

    public async Task<WorkerChannel> WaitForFirstInitializedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            Task initializedChannelAvailable;
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                RpcClientWorkerChannel channel = _slots.Values.FirstOrDefault(slot => slot.Channel is not null)?.Channel;
                if (channel is not null)
                {
                    return channel;
                }

                initializedChannelAvailable = _initializedChannelAvailable.Task;
            }

            await initializedChannelAvailable.WaitAsync(cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            _disposeTask ??= DisposeAsyncCore();
            return new(_disposeTask);
        }
    }

    private async Task DisposeAsyncCore()
    {
        KeyValuePair<string, WorkerSlot>[] slots;
        Task[] monitorTasks;

        lock (_stateLock)
        {
            _disposed = true;
            slots = [.. _slots];
            monitorTasks = [.. _monitorTasks];
            _initializedChannelAvailable.TrySetResult();
        }

        Exception disposalException = null;
        try
        {
            await _shutdownSource.CancelAsync();
        }
        catch (Exception exception)
        {
            disposalException = exception;
        }

        Task<Exception>[] slotDisposals = [.. slots.Select(slot => ShutdownSlotAsync(slot.Key, slot.Value))];
        Exception[] slotExceptions = await Task.WhenAll(slotDisposals);
        foreach (Exception exception in slotExceptions)
        {
            if (exception is not null)
            {
                disposalException = AggregateException.Combine(disposalException, exception);
            }
        }

        await Task.WhenAll(monitorTasks);

        if (disposalException is not null)
        {
            ExceptionDispatchInfo.Capture(disposalException).Throw();
        }
    }

    private RpcClientWorkerChannel DetachChannelLocked(WorkerSlot slot, RpcClientWorkerChannel expectedChannel)
    {
        RpcClientWorkerChannel channel = slot.Channel;
        if (channel is null || (expectedChannel is not null && !ReferenceEquals(channel, expectedChannel)))
        {
            return null;
        }

        slot.Channel = null;
        if (slot.LinkTask is not null)
        {
            slot.Terminal = true;
            slot.LinkTask = null;
        }

        if (!_disposed && !_slots.Values.Any(slot => slot.Channel is not null))
        {
            _initializedChannelAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return channel;
    }

    private void RemoveEmptySlot(string workerId, WorkerSlot slot)
    {
        lock (_stateLock)
        {
            if (slot.Channel is null &&
                _slots.TryGetValue(workerId, out WorkerSlot currentSlot) && ReferenceEquals(currentSlot, slot))
            {
                _slots.Remove(workerId);
            }
        }
    }

    private async Task MonitorChannelCompletionAsync(string workerId, WorkerSlot slot, RpcClientWorkerChannel channel)
    {
        Exception terminalException = null;
        try
        {
            // The Client topology has no local worker process to monitor, so transport completion is its terminal signal.
            await channel.Completion.WaitAsync(_shutdownSource.Token);
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            terminalException = exception;
        }

        SemaphoreLock gate = await slot.Gate.LockAsync();
        try
        {
            using (gate)
            {
                // LinkAsync holds this gate until the monitor is tracked and publication completes. It also serializes this
                // cleanup with unlink and shutdown.
                RpcClientWorkerChannel removedChannel;
                lock (_stateLock)
                {
                    // Only detach the channel associated with this completion signal.
                    removedChannel = DetachChannelLocked(slot, channel);
                }

                if (removedChannel is null)
                {
                    return;
                }

                if (terminalException is null)
                {
                    Log.ChannelCompleted(_logger, workerId);
                }
                else
                {
                    Log.ChannelFailed(_logger, terminalException, workerId);
                }

                try
                {
                    removedChannel.Shutdown(terminalException);
                }
                catch (Exception exception)
                {
                    Log.ChannelShutdownFailed(_logger, exception, workerId);
                }

                try
                {
                    await removedChannel.DisposeAsync();
                }
                catch (Exception exception)
                {
                    Log.ChannelDisposalFailed(_logger, exception, workerId);
                }
            }
        }
        catch (Exception exception)
        {
            Log.ChannelRemovalFailed(_logger, exception, workerId);
        }
        finally
        {
            RemoveEmptySlot(workerId, slot);
        }
    }

    private void OnMonitorCompleted(Task monitorTask)
    {
        lock (_stateLock)
        {
            _monitorTasks.Remove(monitorTask);
        }
    }

    private void RegisterInitializedChannel(string workerId, WorkerSlot slot, RpcClientWorkerChannel channel)
    {
        Task monitorTask;

        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            slot.Channel = channel;
            monitorTask = MonitorChannelCompletionAsync(workerId, slot, channel);
            _monitorTasks.Add(monitorTask);

            // Signal initialized-channel availability without starting ScriptHost here.
            _initializedChannelAvailable.TrySetResult();
        }

        _ = monitorTask.ContinueWith(static (task, state) => ((WorkerChannelRegistry)state).OnMonitorCompleted(task), this,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task<Exception> ShutdownSlotAsync(string workerId, WorkerSlot slot)
    {
        Exception disposalException = null;

        SemaphoreLock gate = await slot.Gate.LockAsync();
        try
        {
            using (gate)
            {
                RpcClientWorkerChannel channel;
                lock (_stateLock)
                {
                    channel = DetachChannelLocked(slot, expectedChannel: null);
                }

                disposalException = await channel.DisposeAndCaptureExceptionAsync(disposalException);
            }
        }
        catch (Exception exception)
        {
            disposalException = AggregateException.Combine(disposalException, exception);
        }
        finally
        {
            RemoveEmptySlot(workerId, slot);
        }

        return disposalException;
    }

    private sealed class WorkerSlot
    {
        public RpcClientWorkerChannel Channel { get; set; }

        // The initial link owns this gate before its slot is published.
        public SemaphoreSlim Gate { get; } = new(0, 1);

        // The recorded gRPC endpoint used to distinguish exact retries from conflicting requests.
        public Uri Endpoint { get; init; }

        // The link task shared by exact retries; cleared when the accepted channel is detached.
        public Task<WorkerChannel> LinkTask { get; set; }

        // Indicates that a previously accepted link has terminated and cannot be replayed.
        public bool Terminal { get; set; }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Debug, "FunctionRpc channel for worker {WorkerId} completed and was removed.")]
        public static partial void ChannelCompleted(ILogger logger, string workerId);

        [LoggerMessage(1, LogLevel.Warning, "FunctionRpc channel for worker {WorkerId} failed and was removed.")]
        public static partial void ChannelFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(2, LogLevel.Error, "Failed to dispose the completed FunctionRpc channel for worker {WorkerId}.")]
        public static partial void ChannelDisposalFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(3, LogLevel.Error, "Failed to remove the completed FunctionRpc channel for worker {WorkerId}.")]
        public static partial void ChannelRemovalFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(4, LogLevel.Error, "Failed to shut down the completed FunctionRpc channel for worker {WorkerId}.")]
        public static partial void ChannelShutdownFailed(ILogger logger, Exception exception, string workerId);
    }
}
