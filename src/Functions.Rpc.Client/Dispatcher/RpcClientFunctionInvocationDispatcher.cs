// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Host.Executors.Internal;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Dispatches function invocations through client-backed worker channels.
/// </summary>
/// <remarks>
/// One dispatcher belongs to a ScriptHost child container. It borrows root-owned channels and never disposes them.
/// </remarks>
internal sealed partial class RpcClientFunctionInvocationDispatcher : IRpcClientFunctionInvocationDispatcher
{
    private static readonly TimeSpan DefaultChannelWaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly IWorkerChannelRegistry _channelRegistry;
    private readonly CancellationTokenSource _dispatcherStoppedSource = new();
    private readonly ILogger<RpcClientFunctionInvocationDispatcher> _logger;
    private readonly ManagedDependencyOptions _managedDependencyOptions;
    private readonly TimeSpan _channelWaitTimeout;
    private readonly ScriptJobHostOptions _scriptHostOptions;

    // Setup results belong to this dispatcher without keeping unlinked channels alive.
    private readonly ConditionalWeakTable<WorkerChannel, TaskCompletionSource> _channelSetups = new();

    // Shutdown waits for active channel setup to finish and prevents new setup.
    private readonly Lock _lifecycleLock = new();

    private IReadOnlyList<FunctionMetadata> _functions = [];
    private Task _laterLinkedChannelsSetup = Task.CompletedTask;
    private int _nextChannelIndex = -1;
    private bool _disposed;
    private bool _stopping;

    public RpcClientFunctionInvocationDispatcher(
        IWorkerChannelRegistry channelRegistry,
        IOptions<ScriptJobHostOptions> scriptHostOptions,
        IOptions<ManagedDependencyOptions> managedDependencyOptions,
        ILogger<RpcClientFunctionInvocationDispatcher> logger)
        : this(channelRegistry, scriptHostOptions, managedDependencyOptions, logger, DefaultChannelWaitTimeout)
    {
    }

    internal RpcClientFunctionInvocationDispatcher(
        IWorkerChannelRegistry channelRegistry,
        IOptions<ScriptJobHostOptions> scriptHostOptions,
        IOptions<ManagedDependencyOptions> managedDependencyOptions,
        ILogger<RpcClientFunctionInvocationDispatcher> logger,
        TimeSpan channelWaitTimeout)
    {
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _scriptHostOptions = scriptHostOptions?.Value ?? throw new ArgumentNullException(nameof(scriptHostOptions));
        _managedDependencyOptions = managedDependencyOptions?.Value ?? throw new ArgumentNullException(nameof(managedDependencyOptions));
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

        // Keep the common path synchronous and allocation-free after the registry snapshot.
        WorkerChannel channel = GetReadyChannel();
        return channel is null
            ? InvokeWhenChannelIsReadyAsync(invocationContext)
            : DispatchInvocation(invocationContext, channel);
    }

    public Task SetupChannelAsync(WorkerChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopping)
            {
                throw new InvalidOperationException("The invocation dispatcher is stopping.");
            }

            if (State is FunctionInvocationDispatcherState.Default)
            {
                throw new InvalidOperationException("The invocation dispatcher has not been initialized.");
            }

            return SetupChannelLocked(channel);
        }
    }

    // Requires _lifecycleLock: a stopping dispatcher must not overwrite the next ScriptHost's buffers.
    private Task SetupChannelLocked(WorkerChannel channel)
    {
        TaskCompletionSource setup = GetChannelSetup(channel);
        if (!setup.Task.IsCompleted)
        {
            try
            {
                channel.SetupFunctionInvocationBuffers(_functions);
                channel.SendFunctionLoadRequests(_managedDependencyOptions, _scriptHostOptions.FunctionTimeout);
                setup.SetResult();
            }
            catch (Exception exception)
            {
                // A partial setup is terminal for this dispatcher. Retrying would replace invocation buffers.
                setup.SetException(exception);
            }
        }

        // Setup is synchronous. Observe and rethrow failures for the caller's existing diagnostics.
        setup.Task.GetAwaiter().GetResult();
        return setup.Task;
    }

    private TaskCompletionSource GetChannelSetup(WorkerChannel channel)
        => _channelSetups.GetValue(channel, static _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

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

    public async Task InitializeAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FunctionMetadata[] functionArray = functions?.ToArray() ?? [];

        if (functionArray.Length == 0)
        {
            Log.NoFunctions(_logger, nameof(RpcClientFunctionInvocationDispatcher));
            return;
        }

        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopping)
            {
                throw new InvalidOperationException("The invocation dispatcher is stopping.");
            }

            _functions = functionArray;
            State = FunctionInvocationDispatcherState.Initializing;
        }

        // Metadata is available before this lifecycle callback, so eagerly load every worker linked during startup.
        // Read the version before the snapshot so the later-link loop cannot miss concurrent links.
        long initializedChannelsVersion = _channelRegistry.InitializedChannelsVersion;
        IReadOnlyList<WorkerChannel> channels = _channelRegistry.GetInitializedChannels();
        List<Task> channelSetupTasks = [];
        foreach (WorkerChannel channel in channels)
        {
            try
            {
                channelSetupTasks.Add(SetupChannelAsync(channel));
            }
            catch (Exception exception)
            {
                Log.ChannelSetupFailed(_logger, exception, channel.Id);
                channelSetupTasks.Add(Task.FromException(exception));
            }
        }

        try
        {
            await Task.WhenAll(channelSetupTasks).WaitAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && channels.Any(IsReadyForInvocations))
        {
            // A failed channel must not prevent startup when another linked channel can serve invocations.
        }

        AddLogUserCategory(functionArray);
        lock (_lifecycleLock)
        {
            // Preserve the shutdown state if setup raced with shutdown.
            if (_disposed || _stopping)
            {
                return;
            }

            State = FunctionInvocationDispatcherState.Initialized;

            // Publish ownership before shutdown can begin, without retaining the startup snapshot.
            HashSet<WorkerChannel> attemptedChannels = new(channels, ReferenceEqualityComparer.Instance);
            _laterLinkedChannelsSetup = SetupLaterLinkedChannelsAsync(initializedChannelsVersion, attemptedChannels);
        }
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

        Task laterLinkedChannelsSetup;
        lock (_lifecycleLock)
        {
            laterLinkedChannelsSetup = _laterLinkedChannelsSetup;
        }

        await laterLinkedChannelsSetup;

        // The registry owns channel disposal; the dispatcher only gives accepted invocations time to drain.
        Task[] drainTasks = [.. _channelRegistry.GetInitializedChannels()
            .Where(channel => channel.IsChannelReadyForInvocations())
            .Select(channel => channel.DrainInvocationsAsync())];

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
        // Ready-channel invocations already racing shutdown may still be accepted and drained.
        lock (_lifecycleLock)
        {
            if (_disposed || _stopping)
            {
                return;
            }

            _stopping = true;
            State = FunctionInvocationDispatcherState.Disposing;
        }

        _dispatcherStoppedSource.Cancel();
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopping = true;
            State = FunctionInvocationDispatcherState.Disposed;
        }

        _dispatcherStoppedSource.Cancel();
    }

    private WorkerChannel GetReadyChannel()
    {
        IReadOnlyList<WorkerChannel> channels = _channelRegistry.GetInitializedChannels();
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

    private async Task InvokeWhenChannelIsReadyAsync(ScriptInvocationContext invocationContext)
    {
        // Initialization checks and cancellation linking stay off the ready-channel hot path.
        if (State is not FunctionInvocationDispatcherState.Initialized)
        {
            throw new InvalidOperationException("The invocation dispatcher has not been initialized.");
        }

        using CancellationTokenSource operationSource =
            CancellationTokenSource.CreateLinkedTokenSource(invocationContext.CancellationToken, _dispatcherStoppedSource.Token);
        operationSource.CancelAfter(_channelWaitTimeout);
        try
        {
            WorkerChannel initializedChannel = await _channelRegistry.WaitForFirstInitializedAsync(operationSource.Token)
                .WaitAsync(operationSource.Token);
            await GetChannelSetup(initializedChannel).Task.WaitAsync(operationSource.Token);
        }
        catch (OperationCanceledException) when (operationSource.IsCancellationRequested &&
            !invocationContext.CancellationToken.IsCancellationRequested &&
            !_dispatcherStoppedSource.IsCancellationRequested)
        {
            throw new TimeoutException($"No client-backed worker channel became ready within {_channelWaitTimeout}.");
        }

        // A linked channel is selectable only after its buffers and load requests are configured successfully.
        WorkerChannel channel = GetReadyChannel()
            ?? throw new InvalidOperationException("No client-backed worker channel is ready for invocations.");
        await DispatchInvocation(invocationContext, channel);
    }

    private bool IsReadyForInvocations(WorkerChannel channel)
        => _channelSetups.TryGetValue(channel, out TaskCompletionSource setup)
            && setup.Task.IsCompletedSuccessfully
            && channel.IsChannelReadyForInvocations();

    private async Task SetupLaterLinkedChannelsAsync(long initializedChannelsVersion, HashSet<WorkerChannel> attemptedChannels)
    {
        CancellationToken cancellationToken = _dispatcherStoppedSource.Token;
        try
        {
            // Let initialization publish the task under _lifecycleLock before the watcher can perform any work.
            await Task.Yield();
            while (true)
            {
                initializedChannelsVersion = await _channelRegistry.WaitForInitializedChannelsChangeAsync(initializedChannelsVersion, cancellationToken);
                ReconcileInitializedChannels(attemptedChannels);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
            // Registry disposal is expected during Host shutdown.
        }
        catch (Exception exception)
        {
            Log.LaterLinkSetupFailed(_logger, exception);
        }
        finally
        {
            // A retained completed task must not retain channels through its state machine.
            attemptedChannels.Clear();
        }
    }

    // Keep snapshots out of the watcher's async state machine, including in Debug builds.
    private void ReconcileInitializedChannels(HashSet<WorkerChannel> attemptedChannels)
    {
        IReadOnlyList<WorkerChannel> channels = _channelRegistry.GetInitializedChannels();
        attemptedChannels.IntersectWith(channels);
        foreach (WorkerChannel channel in channels)
        {
            if (!attemptedChannels.Add(channel))
            {
                continue;
            }

            try
            {
                lock (_lifecycleLock)
                {
                    // If shutdown began after the loop woke, leave setup to the next ScriptHost.
                    if (_stopping)
                    {
                        return;
                    }

                    Log.SettingUpLaterLinkedChannel(_logger, channel.Id);
                    SetupChannelLocked(channel);
                }
            }
            catch (Exception exception)
            {
                Log.ChannelSetupFailed(_logger, exception, channel.Id);
            }
        }
    }

    private void AddLogUserCategory(IEnumerable<FunctionMetadata> functions)
    {
        foreach (FunctionMetadata metadata in functions)
        {
            metadata.Properties[LogConstants.CategoryNameKey] = LogCategories.CreateFunctionUserCategory(metadata.Name);
            metadata.Properties[ScriptConstants.LogPropertyHostInstanceIdKey] = _scriptHostOptions.InstanceId;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Trace, "Posting invocation {InvocationId} on worker {WorkerId}.")]
        public static partial void PostingInvocation(ILogger logger, Guid invocationId, string workerId);

        [LoggerMessage(1, LogLevel.Debug, "{Dispatcher} received no functions.")]
        public static partial void NoFunctions(ILogger logger, string dispatcher);

        [LoggerMessage(2, LogLevel.Debug, "Draining client-backed worker invocations timed out during dispatcher shutdown.")]
        public static partial void DrainTimedOut(ILogger logger);

        [LoggerMessage(3, LogLevel.Warning, "Failed to configure client-backed worker {WorkerId} for invocations.")]
        public static partial void ChannelSetupFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(4, LogLevel.Information, "Configuring client-backed worker {WorkerId}, linked after dispatcher initialization, for invocations.")]
        public static partial void SettingUpLaterLinkedChannel(ILogger logger, string workerId);

        [LoggerMessage(5, LogLevel.Error, "Setup of client-backed workers linked after dispatcher initialization stopped unexpectedly. Workers linked later will not receive invocations until ScriptHost restarts.")]
        public static partial void LaterLinkSetupFailed(ILogger logger, Exception exception);
    }
}
