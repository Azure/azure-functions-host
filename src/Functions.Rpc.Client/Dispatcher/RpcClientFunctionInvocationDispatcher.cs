// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Host.Executors.Internal;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Dispatches function invocations through client-backed worker channels.
/// </summary>
/// <remarks>
/// One dispatcher belongs to a ScriptHost child container. It tracks only that ScriptHost's invocation lifecycle,
/// borrows root-owned channels already configured by the registry, and never disposes them.
/// </remarks>
internal sealed partial class RpcClientFunctionInvocationDispatcher : IRpcClientFunctionInvocationDispatcher
{
    private static readonly TimeSpan DefaultChannelWaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly IWorkerChannelRegistry _channelRegistry;
    private readonly CancellationTokenSource _dispatcherStoppedSource = new();
    private readonly ILogger<RpcClientFunctionInvocationDispatcher> _logger;
    private readonly TimeSpan _channelWaitTimeout;
    private int _nextChannelIndex = -1;
    private bool _disposed;
    private bool _stopping;

    public RpcClientFunctionInvocationDispatcher(
        IWorkerChannelRegistry channelRegistry,
        ILogger<RpcClientFunctionInvocationDispatcher> logger)
        : this(channelRegistry, logger, DefaultChannelWaitTimeout)
    {
    }

    internal RpcClientFunctionInvocationDispatcher(
        IWorkerChannelRegistry channelRegistry,
        ILogger<RpcClientFunctionInvocationDispatcher> logger,
        TimeSpan channelWaitTimeout)
    {
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (channelWaitTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(channelWaitTimeout), "The channel wait timeout must be greater than zero.");
        }

        _channelWaitTimeout = channelWaitTimeout;
    }

    public FunctionInvocationDispatcherState State { get; private set; }

    public int ErrorEventsThreshold => 3;

    public Task InvokeAsync(ScriptInvocationContext invocationContext)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stopping)
        {
            throw new InvalidOperationException("The invocation dispatcher is stopping.");
        }

        if (State is not FunctionInvocationDispatcherState.Initialized)
        {
            throw new InvalidOperationException("The invocation dispatcher has not been initialized.");
        }

        // Keep the common path synchronous and allocation-free after the registry snapshot.
        ValueTask<IReadOnlyList<WorkerChannel>> readyChannels = _channelRegistry.GetReadyChannelsAsync();
        if (readyChannels.IsCompletedSuccessfully)
        {
            WorkerChannel channel = GetReadyChannel(readyChannels.Result);
            if (channel is not null)
            {
                return DispatchInvocation(invocationContext, channel);
            }
        }

        return InvokeWhenChannelIsReadyAsync(invocationContext, readyChannels);
    }

    private Task DispatchInvocation(ScriptInvocationContext invocationContext, WorkerChannel channel)
    {
        using FunctionInvoker.Scope scope = FunctionInvoker.BeginSystemScope();
        string functionId = invocationContext.FunctionMetadata.GetFunctionId();
        if (channel.FunctionInputBuffers.TryGetValue(functionId, out BufferBlock<ScriptInvocationContext> inputBuffer))
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                Log.PostingInvocation(_logger, invocationContext.ExecutionContext.InvocationId, channel.Id);
            }

            inputBuffer.Post(invocationContext);
            return Task.CompletedTask;
        }

        throw new InvalidOperationException(
            $"Function '{invocationContext.FunctionMetadata.Name}' is not loaded by worker '{channel.Id}'.");
    }

    public Task InitializeAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stopping)
        {
            throw new InvalidOperationException("The invocation dispatcher is stopping.");
        }

        if (functions is null || !functions.Any())
        {
            Log.NoFunctions(_logger, nameof(RpcClientFunctionInvocationDispatcher));
            return Task.CompletedTask;
        }

        State = FunctionInvocationDispatcherState.Initializing;
        State = FunctionInvocationDispatcherState.Initialized;

        return Task.CompletedTask;
    }

    public async Task<IDictionary<string, WorkerStatus>> GetWorkerStatusesAsync()
    {
        // Preserve snapshot order so each status remains paired with the channel that produced it.
        IReadOnlyList<WorkerChannel> channels = _channelRegistry.GetInitializedChannels();
        WorkerStatus[] statuses = await Task.WhenAll(channels.Select(channel => channel.GetWorkerStatusAsync()));

        Dictionary<string, WorkerStatus> result = new(StringComparer.Ordinal);
        for (int i = 0; i < channels.Count; i++)
        {
            result.Add(channels[i].Id, statuses[i]);
        }

        return result;
    }

    public async Task ShutdownAsync()
    {
        PreShutdown();

        // Existing invocations must drain even if replacement setup invalidated the active ready snapshot.
        // The registry owns disposal; shutdown never waits for a future channel.
        List<Task> drainTasks = [];
        foreach (WorkerChannel channel in _channelRegistry.GetInitializedChannels())
        {
            if (IsReadyForInvocations(channel))
            {
                drainTasks.Add(channel.DrainInvocationsAsync());
            }
        }

        try
        {
            await Task.WhenAll(drainTasks).WaitAsync(DefaultShutdownTimeout);
        }
        catch (TimeoutException)
        {
            Log.DrainTimedOut(_logger);
        }
    }

    public Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception)
        => Task.FromResult(false);

    public Task StartWorkerChannel() => Task.CompletedTask;

    public void PreShutdown()
    {
        if (_disposed || _stopping)
        {
            return;
        }

        // This latch is best effort. A ready-channel invocation racing shutdown may still be accepted and drained.
        _stopping = true;
        State = FunctionInvocationDispatcherState.Disposing;
        _dispatcherStoppedSource.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping = true;
        State = FunctionInvocationDispatcherState.Disposed;
        _dispatcherStoppedSource.Cancel();
    }

    private WorkerChannel GetReadyChannel(IReadOnlyList<WorkerChannel> channels)
    {
        if (channels.Count == 0)
        {
            return null;
        }

        if (channels.Count == 1)
        {
            // The common one-worker path avoids round-robin synchronization and scanning.
            WorkerChannel channel = channels[0];
            return IsReadyForInvocations(channel) ? channel : null;
        }

        // Start each scan at the next channel to spread work without allocating or locking.
        int startIndex = (int)((uint)Interlocked.Increment(ref _nextChannelIndex) % (uint)channels.Count);
        for (int offset = 0; offset < channels.Count; offset++)
        {
            WorkerChannel channel = channels[(startIndex + offset) % channels.Count];
            if (IsReadyForInvocations(channel))
            {
                return channel;
            }
        }

        return null;
    }

    private async Task InvokeWhenChannelIsReadyAsync(
        ScriptInvocationContext invocationContext,
        ValueTask<IReadOnlyList<WorkerChannel>> readyChannels)
    {
        // Cancellation linking stays off the ready-channel hot path.
        using CancellationTokenSource operationSource =
            CancellationTokenSource.CreateLinkedTokenSource(invocationContext.CancellationToken, _dispatcherStoppedSource.Token);
        operationSource.CancelAfter(_channelWaitTimeout);
        try
        {
            while (true)
            {
                // Completed snapshots bypass WaitAsync, so every retry must explicitly observe cancellation.
                operationSource.Token.ThrowIfCancellationRequested();
                IReadOnlyList<WorkerChannel> channels = readyChannels.IsCompletedSuccessfully
                    ? readyChannels.Result
                    : await readyChannels.AsTask().WaitAsync(operationSource.Token);
                WorkerChannel channel = GetReadyChannel(channels);
                if (channel is not null)
                {
                    operationSource.Token.ThrowIfCancellationRequested();
                    await DispatchInvocation(invocationContext, channel);
                    return;
                }

                // A signaled channel can stop before selection; ask for current readiness instead of polling it.
                readyChannels = _channelRegistry.GetReadyChannelsAsync();
            }
        }
        catch (OperationCanceledException) when (!invocationContext.CancellationToken.IsCancellationRequested &&
            !_dispatcherStoppedSource.IsCancellationRequested)
        {
            throw new TimeoutException($"No client-backed worker channel became ready within {_channelWaitTimeout}.");
        }
    }

    private static bool IsReadyForInvocations(WorkerChannel channel) => channel is RpcClientWorkerChannel { IsReadyForInvocations: true };

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Trace, "Posting invocation {InvocationId} on worker {WorkerId}.")]
        public static partial void PostingInvocation(ILogger logger, Guid invocationId, string workerId);

        [LoggerMessage(1, LogLevel.Debug, "{Dispatcher} received no functions.")]
        public static partial void NoFunctions(ILogger logger, string dispatcher);

        [LoggerMessage(2, LogLevel.Debug, "Draining client-backed worker invocations timed out during dispatcher shutdown.")]
        public static partial void DrainTimedOut(ILogger logger);
    }
}
