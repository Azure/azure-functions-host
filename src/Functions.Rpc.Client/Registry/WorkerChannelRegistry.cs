// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Owns initialized client-backed worker channels, ScriptHost setup, and terminal cleanup.
/// </summary>
/// <remarks>
/// Admission and replay decisions are atomic. Different workers can link concurrently.
/// The registry publishes a channel after its WorkerInit handshake succeeds, configures it for the active ScriptHost
/// context, and retains terminal identities. Initialized channels remain available for metadata bootstrap before a
/// ScriptHost function set is known; only ready channels are available for invocation dispatch.
/// </remarks>
internal sealed partial class WorkerChannelRegistry :
    IWorkerChannelRegistry,
    IHostStateCoordinator,
    IHttpWorkerScaleInManager,
    IRuntimeWorkerAdmissionGate
{
    private static readonly TimeSpan DefaultLinkTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GracefulUnlinkTimeout = TimeSpan.FromSeconds(10);

    private readonly IRpcClientWorkerChannelFactory _channelFactory;
    private readonly Lock _disposeLock = new();
    private readonly IDuplexChannelFactory<StreamingMessage> _duplexChannelFactory;
    private readonly HostStateClock _hostStateClock;
    private readonly ILogger<WorkerChannelRegistry> _logger;
    private readonly Dictionary<string, WorkerSlot> _linkAttempts = new(StringComparer.Ordinal);
    private readonly TimeSpan _linkTimeout;
    private readonly HashSet<Task> _monitorTasks = [];
    private readonly HashSet<WorkerSlot> _physicallyLinkedSlots = [];
    private readonly CancellationTokenSource _shutdownSource = new();
    private readonly Dictionary<string, WorkerSlot> _slots = new(StringComparer.Ordinal);
    private readonly Lock _stateLock = new();
    private ChannelSetupContext _activeSetupContext;
    private Task _disposeTask;
    private bool _disposed;
    private TaskCompletionSource<long> _hostStateChanged =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TaskCompletionSource _initializedChannelAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _runtimeAdmissionClosed;

    // Setup writers hold _stateLock; ready-channel readers observe this published signal without taking that lock.
    private TaskCompletionSource<IReadOnlyList<WorkerChannel>> _readyChannelsAvailable = CreateReadyChannelsSignal();

    public WorkerChannelRegistry(
        IDuplexChannelFactory<StreamingMessage> duplexChannelFactory,
        IRpcClientWorkerChannelFactory channelFactory,
        ILogger<WorkerChannelRegistry> logger)
        : this(duplexChannelFactory, channelFactory, logger, DefaultLinkTimeout, TimeProvider.System)
    {
    }

    public WorkerChannelRegistry(
        IDuplexChannelFactory<StreamingMessage> duplexChannelFactory,
        IRpcClientWorkerChannelFactory channelFactory,
        ILogger<WorkerChannelRegistry> logger,
        TimeProvider timeProvider)
        : this(duplexChannelFactory, channelFactory, logger, DefaultLinkTimeout, timeProvider)
    {
    }

    internal WorkerChannelRegistry(
        IDuplexChannelFactory<StreamingMessage> duplexChannelFactory,
        IRpcClientWorkerChannelFactory channelFactory,
        ILogger<WorkerChannelRegistry> logger,
        TimeSpan linkTimeout,
        TimeProvider timeProvider = null)
    {
        _duplexChannelFactory = duplexChannelFactory ?? throw new ArgumentNullException(nameof(duplexChannelFactory));
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hostStateClock = new(timeProvider ?? TimeProvider.System);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(linkTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(linkTimeout.TotalMilliseconds, uint.MaxValue - 1);
        _linkTimeout = linkTimeout;
    }

    HostStateSnapshot IHostStateCoordinator.Current
    {
        get
        {
            lock (_stateLock)
            {
                RefreshHostStateLocked();
                return _hostStateClock.Current;
            }
        }
    }

    long IHostStateCoordinator.Version
    {
        get
        {
            lock (_stateLock)
            {
                RefreshHostStateLocked();
                return _hostStateClock.Current.Version;
            }
        }
    }

    bool IRuntimeWorkerAdmissionGate.IsClosed
    {
        get
        {
            lock (_stateLock)
            {
                return _runtimeAdmissionClosed;
            }
        }
    }

    Task<long> IHostStateCoordinator.WaitForChangeAsync(
        long version,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RefreshHostStateLocked();
            if (_hostStateClock.Current.Version != version)
            {
                return Task.FromResult(_hostStateClock.Current.Version);
            }

            return _hostStateChanged.Task.WaitAsync(cancellationToken);
        }
    }

    bool IRuntimeWorkerAdmissionGate.TryClose()
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runtimeAdmissionClosed)
            {
                return false;
            }

            _runtimeAdmissionClosed = true;
            SignalHostStateChangedLocked();
            return true;
        }
    }

    public Task<WorkerLinkResult> LinkAsync(
        string workerId,
        Uri grpcEndpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        RpcClientFactory.ValidateEndpoint(grpcEndpoint);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<WorkerChannel> linkCompletion;
        WorkerSlot slot;
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

                if (existing.Terminal || existing.Channel is { IsTerminated: true })
                {
                    throw new WorkerLinkException(WorkerLinkFailureReason.WorkerTerminated,
                        "The worker's initialized channel has terminated.");
                }

                return CompleteLinkAsync(existing.LinkTask.WaitAsync(_linkTimeout, cancellationToken), isNewLink: false);
            }

            if (_runtimeAdmissionClosed)
            {
                throw new WorkerLinkException(
                    WorkerLinkFailureReason.RuntimeStopping,
                    "The Runtime has permanently closed new Worker-Link admission.");
            }

            linkCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            slot = new() { Endpoint = grpcEndpoint, LinkTask = linkCompletion.Task };
            _slots.Add(workerId, slot);
            _linkAttempts.Add(workerId, slot);
            _physicallyLinkedSlots.Add(slot);
            SignalHostStateChangedLocked();
        }

        // The shared completion is published before I/O begins, and connection work runs outside the state lock.
        _ = LinkAndCompleteAsync(workerId, grpcEndpoint, slot, linkCompletion, cancellationToken);

        return CompleteLinkAsync(linkCompletion.Task, isNewLink: true);
    }

    private static async Task<WorkerLinkResult> CompleteLinkAsync(Task<WorkerChannel> link, bool isNewLink)
        => new(await link.ConfigureAwait(false), isNewLink);

    public HttpWorkerScaleInReservation TryReserveWorker()
    {
        lock (_stateLock)
        {
            if (_disposed || _runtimeAdmissionClosed)
            {
                return null;
            }

            string[] workerIds = [.. _slots.Keys];
            Array.Sort(workerIds, StringComparer.Ordinal);
            foreach (string workerId in workerIds)
            {
                WorkerSlot slot = _slots[workerId];
                if (slot.Channel is not { } channel ||
                    slot.DrainTask is not null ||
                    !channel.TryReserveScaleIn(out Guid reservationId))
                {
                    continue;
                }

                SignalReadyChannelsLocked();
                SignalHostStateChangedLocked();
                return new(workerId, reservationId, _hostStateClock.Current);
            }

            return null;
        }
    }

    public bool TryCommitReservation(
        HttpWorkerScaleInReservation reservation,
        out Task<bool> drainCompletion)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        Task invocationDrainTask;
        TaskCompletionSource<bool> completion;
        WorkerSlot slot;
        RpcClientWorkerChannel channel;
        lock (_stateLock)
        {
            RefreshHostStateLocked();
            HostStateSnapshot currentState = _hostStateClock.Current;
            if (_disposed ||
                _runtimeAdmissionClosed ||
                currentState.Version != reservation.ProjectedState.Version ||
                currentState.HttpCapacity != reservation.ProjectedState.HttpCapacity ||
                currentState.LinkedWorkerCount != reservation.ProjectedState.LinkedWorkerCount ||
                !_slots.TryGetValue(reservation.WorkerId, out slot) ||
                slot.Channel is not { } currentChannel ||
                slot.DrainTask is not null ||
                currentChannel.CommitScaleInReservation(reservation.ReservationId) is not { } committedInvocations)
            {
                drainCompletion = null;
                return false;
            }

            channel = currentChannel;
            invocationDrainTask = committedInvocations;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            drainCompletion = completion.Task;
            slot.DrainTask = drainCompletion;
            SignalReadyChannelsLocked();
        }

        _ = CompleteDrainAsync(reservation.WorkerId, slot, channel, invocationDrainTask, completion);
        return true;
    }

    public bool CancelReservation(
        HttpWorkerScaleInReservation reservation,
        out bool invocationAdmissionRestored)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        invocationAdmissionRestored = false;

        lock (_stateLock)
        {
            bool restoreInvocationAdmission = !_runtimeAdmissionClosed;
            if (_disposed ||
                !_slots.TryGetValue(reservation.WorkerId, out WorkerSlot slot) ||
                slot.Channel is not { } channel ||
                !channel.CancelScaleInReservation(
                    reservation.ReservationId,
                    restoreInvocationAdmission))
            {
                return false;
            }

            invocationAdmissionRestored = restoreInvocationAdmission;
            SignalReadyChannelsLocked();
            SignalHostStateChangedLocked();
            return true;
        }
    }

    private async Task LinkAndCompleteAsync(string workerId, Uri grpcEndpoint, WorkerSlot slot, TaskCompletionSource<WorkerChannel> linkCompletion,
        CancellationToken cancellationToken)
    {
        try
        {
            WorkerChannel channel = await LinkCoreAsync(workerId, grpcEndpoint, slot, cancellationToken);
            linkCompletion.SetResult(channel);
        }
        catch (OperationCanceledException exception)
        {
            linkCompletion.SetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            linkCompletion.SetException(exception);
        }
    }

    private async Task<WorkerChannel> LinkCoreAsync(
        string workerId,
        Uri grpcEndpoint,
        WorkerSlot slot,
        CancellationToken cancellationToken)
    {
        using SemaphoreLock gate = new(slot.Gate);
        bool initialized = false;
        try
        {
            using CancellationTokenSource deadline = new(_linkTimeout);
            using CancellationTokenSource operationSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownSource.Token, deadline.Token);

            DuplexChannel<StreamingMessage> ownedChannel = null;
            RpcClientWorkerChannel candidate = null;

            try
            {
                operationSource.Token.ThrowIfCancellationRequested();
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
                RegisterInitializedChannel(workerId, slot, candidate, operationSource.Token);

                WorkerChannel linkedChannel = candidate;
                candidate = null;
                initialized = true;

                return linkedChannel;
            }
            catch (Exception exception)
            {
                Exception failure = exception switch
                {
                    OperationCanceledException when !cancellationToken.IsCancellationRequested && _shutdownSource.IsCancellationRequested
                        => new ObjectDisposedException(GetType().FullName),
                    OperationCanceledException when !cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested
                        => new TimeoutException("The worker link initialization deadline expired.", exception),
                    _ => exception,
                };
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
            // Accepted links are cleaned up by monitor/unlink; completing Link must not wait for deferred setup.
            if (!initialized)
            {
                RemoveEmptySlot(workerId, slot);
                lock (_stateLock)
                {
                    if (_physicallyLinkedSlots.Remove(slot))
                    {
                        SignalHostStateChangedLocked();
                    }

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

                    await DisposeLinkedChannelAsync(slot, channel);
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

    public Task ActivateAsync(IReadOnlyList<FunctionMetadata> functions, ManagedDependencyOptions managedDependencyOptions, TimeSpan? functionTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(managedDependencyOptions);
        cancellationToken.ThrowIfCancellationRequested();

        ChannelSetupContext setupContext = new(functions, managedDependencyOptions, functionTimeout);
        Exception firstSetupException = null;
        int attemptedChannels = 0;
        int readyChannels = 0;

        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            // Invalidate the prior ready snapshot before replacing the context, including an empty function set.
            // An incomplete signal is retained so callers already waiting are not orphaned by a ScriptHost restart.
            ResetReadyChannelsSignal();
            _activeSetupContext = setupContext;

            foreach (WorkerSlot slot in _slots.Values)
            {
                if (slot.Channel is null)
                {
                    continue;
                }

                slot.Channel.SetScaleInReady(false);
                attemptedChannels++;
                ConfigureSlotLocked(slot);
                if (slot.SetupException is null)
                {
                    readyChannels++;
                }
                else
                {
                    firstSetupException ??= slot.SetupException;
                }
            }
        }

        if (attemptedChannels > 0 && readyChannels == 0)
        {
            // Preserve the original failure; a single healthy channel otherwise lets this ScriptHost start.
            ExceptionDispatchInfo.Capture(firstSetupException).Throw();
        }

        return Task.CompletedTask;
    }

    public ValueTask<IReadOnlyList<WorkerChannel>> GetReadyChannelsAsync()
    {
        // Retry only when another thread changes the signal before our CompareExchange succeeds; never poll for readiness.
        // Rereading preserves the newer publication. Waiting callers receive a task instead of spinning.
        while (true)
        {
            // Never wait for the setup lock. The published task is either a snapshot or the shared readiness wait.
            TaskCompletionSource<IReadOnlyList<WorkerChannel>> signal = GetReadyChannelsSignal();
            if (!signal.Task.IsCompletedSuccessfully)
            {
                return new(signal.Task);
            }

            IReadOnlyList<WorkerChannel> readyChannels = signal.Task.Result;
            if (readyChannels.Any(channel => channel is not RpcClientWorkerChannel { IsReadyForInvocations: true }))
            {
                // Transport completion can precede registry cleanup, so exclude channels that stopped since publication.
                readyChannels = [.. readyChannels.Where(channel => channel is RpcClientWorkerChannel { IsReadyForInvocations: true })];
            }

            if (readyChannels.Count > 0)
            {
                return new(readyChannels);
            }

            // Replace a stale completed signal only if a setup/cleanup writer has not published a newer snapshot.
            TaskCompletionSource<IReadOnlyList<WorkerChannel>> replacement = CreateReadyChannelsSignal();
            TaskCompletionSource<IReadOnlyList<WorkerChannel>> observedSignal =
                Interlocked.CompareExchange(ref _readyChannelsAvailable, replacement, signal);

            if (!ReferenceEquals(observedSignal, signal))
            {
                // Another thread changed the signal; retry against the newer publication.
                continue;
            }

            return new(replacement.Task);
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
            _hostStateChanged.TrySetException(new ObjectDisposedException(GetType().FullName));
            _initializedChannelAvailable.TrySetResult();

            // Publish disposal for new readers and release callers awaiting the previous shared signal.
            ObjectDisposedException exception = new(GetType().FullName);
            TaskCompletionSource<IReadOnlyList<WorkerChannel>> disposedSignal = CreateReadyChannelsSignal();
            disposedSignal.SetException(exception);
            Interlocked.Exchange(ref _readyChannelsAvailable, disposedSignal).TrySetException(exception);
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
        slot.AttemptedSetupContext = null;
        slot.SetupException = null;
        if (slot.LinkTask is not null)
        {
            slot.Terminal = true;
            slot.LinkTask = null;
        }

        if (!_disposed)
        {
            // Remove the detached channel from the published snapshot before channel disposal begins.
            SignalReadyChannelsLocked();
            SignalHostStateChangedLocked();
            if (!_slots.Values.Any(slot => slot.Channel is not null))
            {
                _initializedChannelAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
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
            long capacityVersion = channel.HostCapacityVersion;
            while (true)
            {
                Task<long> capacityChange =
                    channel.WaitForHostCapacityChangeAsync(capacityVersion, _shutdownSource.Token);
                Task completed = await Task.WhenAny(
                    channel.Completion,
                    channel.FunctionLoadFailure,
                    capacityChange).WaitAsync(_shutdownSource.Token);
                if (ReferenceEquals(completed, capacityChange))
                {
                    capacityVersion = await capacityChange;
                    lock (_stateLock)
                    {
                        if (_disposed ||
                            !_slots.TryGetValue(workerId, out WorkerSlot currentSlot) ||
                            !ReferenceEquals(currentSlot, slot) ||
                            !ReferenceEquals(slot.Channel, channel))
                        {
                            return;
                        }

                        SignalHostStateChangedLocked();
                    }

                    continue;
                }

                if (channel.FunctionLoadFailure.IsCompletedSuccessfully)
                {
                    terminalException = channel.FunctionLoadFailure.Result;
                }
                else
                {
                    await channel.Completion;
                }

                break;
            }
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
                    removedChannel.FailCommittedInvocations(terminalException);
                    removedChannel.Shutdown(terminalException);
                }
                catch (Exception exception)
                {
                    Log.ChannelShutdownFailed(_logger, exception, workerId);
                }

                try
                {
                    await DisposeLinkedChannelAsync(slot, removedChannel);
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

    private async Task CompleteDrainAsync(
        string workerId,
        WorkerSlot slot,
        RpcClientWorkerChannel channel,
        Task invocationDrainTask,
        TaskCompletionSource<bool> drainCompletion)
    {
        try
        {
            await invocationDrainTask.WaitAsync(_shutdownSource.Token);
            bool unlinked = await GracefullyUnlinkAsync(workerId, slot, channel, _shutdownSource.Token);
            drainCompletion.TrySetResult(unlinked);
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
        {
            drainCompletion.TrySetResult(false);
        }
        catch (Exception exception)
        {
            drainCompletion.TrySetException(exception);
        }
    }

    private async Task<bool> GracefullyUnlinkAsync(
        string workerId,
        WorkerSlot slot,
        RpcClientWorkerChannel expectedChannel,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource operationSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownSource.Token);
        SemaphoreLock gate = await slot.Gate.LockAsync(operationSource.Token);
        try
        {
            using (gate)
            {
                RpcClientWorkerChannel channel;
                lock (_stateLock)
                {
                    channel = DetachChannelLocked(slot, expectedChannel);
                    if (channel is null)
                    {
                        return slot.Terminal;
                    }
                }

                bool peerClosed = true;
                Exception disposalException = null;
                try
                {
                    channel.CompleteRequests();
                    await channel.Completion.WaitAsync(GracefulUnlinkTimeout, operationSource.Token);
                }
                catch (TimeoutException exception)
                {
                    peerClosed = false;
                    Log.GracefulUnlinkTimedOut(_logger, exception, workerId);
                }
                catch (Exception exception) when (channel.Completion.IsCompleted)
                {
                    Log.ChannelFailed(_logger, exception, workerId);
                }
                catch (Exception exception)
                {
                    disposalException = exception;
                }

                disposalException = await DisposeLinkedChannelAndCaptureExceptionAsync(
                    slot,
                    channel,
                    disposalException);
                if (disposalException is not null)
                {
                    ExceptionDispatchInfo.Capture(disposalException).Throw();
                }

                return peerClosed;
            }
        }
        finally
        {
            RemoveEmptySlot(workerId, slot);
        }
    }

    private void RegisterInitializedChannel(string workerId, WorkerSlot slot, RpcClientWorkerChannel channel, CancellationToken cancellationToken)
    {
        Task monitorTask;

        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runtimeAdmissionClosed)
            {
                throw new WorkerLinkException(
                    WorkerLinkFailureReason.RuntimeStopping,
                    "The Runtime closed Worker-Link admission while this link was initializing.");
            }

            // Setup can hold this lock past the link deadline or transport lifetime.
            cancellationToken.ThrowIfCancellationRequested();
            if (channel.IsTerminated)
            {
                Exception failure = channel.FunctionLoadFailure.IsCompletedSuccessfully
                    ? channel.FunctionLoadFailure.Result
                    : channel.Completion.Exception?.GetBaseException();
                throw new IOException("The worker channel terminated before initialization was registered.", failure);
            }

            slot.Channel = channel;
            SignalHostStateChangedLocked();
            monitorTask = MonitorChannelCompletionAsync(workerId, slot, channel);
            _monitorTasks.Add(monitorTask);

            // Signal initialized-channel availability without starting ScriptHost here.
            _initializedChannelAvailable.TrySetResult();
        }

        _ = ConfigureSlotInBackgroundAsync(slot);
        _ = monitorTask.ContinueWith(static (task, state) => ((WorkerChannelRegistry)state).OnMonitorCompleted(task), this,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task ConfigureSlotInBackgroundAsync(WorkerSlot slot)
    {
        // Keep function setup out of the WorkerInit/link call path.
        await Task.Yield();

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            ConfigureSlotLocked(slot);
        }
    }

    private void ConfigureSlotLocked(WorkerSlot slot)
    {
        // Read the context when deferred setup runs, not when the channel linked, to handle ScriptHost restarts.
        ChannelSetupContext setupContext = _activeSetupContext;
        RpcClientWorkerChannel channel = slot.Channel;
        if (setupContext is null ||
            channel is null ||
            ReferenceEquals(slot.AttemptedSetupContext, setupContext))
        {
            return;
        }

        // Each slot gets one attempt per context. Failed setup stays excluded until a new activation retries it.
        slot.AttemptedSetupContext = setupContext;
        slot.SetupException = null;
        try
        {
            channel.TrackScaleInFunctionLoads(setupContext.Functions);
            channel.SetupFunctionInvocationBuffers(setupContext.Functions);
            channel.SendFunctionLoadRequests(setupContext.ManagedDependencyOptions, setupContext.FunctionTimeout);
            channel.SetScaleInReady(true);
            SignalReadyChannelsLocked();
            SignalHostStateChangedLocked();
        }
        catch (Exception exception)
        {
            channel.SetScaleInReady(false);
            slot.SetupException = exception;
            Log.ChannelSetupFailed(_logger, exception, channel.Id);
            SignalHostStateChangedLocked();
        }
    }

    private IReadOnlyList<WorkerChannel> GetReadyChannelsLocked()
    {
        ChannelSetupContext setupContext = _activeSetupContext;
        if (setupContext is null)
        {
            return [];
        }

        // Invocation buffers alone are insufficient: sending load requests must also succeed for this context.
        return [.. _slots.Values
            .Where(slot =>
                ReferenceEquals(slot.AttemptedSetupContext, setupContext) &&
                slot.SetupException is null &&
                slot.Channel is { IsReadyForInvocations: true })
            .Select(slot => (WorkerChannel)slot.Channel)];
    }

    private void SignalReadyChannelsLocked()
    {
        IReadOnlyList<WorkerChannel> readyChannels = GetReadyChannelsLocked();
        if (readyChannels.Count == 0)
        {
            ResetReadyChannelsSignal();
            return;
        }

        // Publish the current snapshot for new readers, then complete the shared wait with the same snapshot.
        TaskCompletionSource<IReadOnlyList<WorkerChannel>> readySignal = CreateReadyChannelsSignal();
        readySignal.SetResult(readyChannels);
        Interlocked.Exchange(ref _readyChannelsAvailable, readySignal).TrySetResult(readyChannels);
    }

    private void ResetReadyChannelsSignal()
    {
        while (true)
        {
            TaskCompletionSource<IReadOnlyList<WorkerChannel>> signal = GetReadyChannelsSignal();
            if (!signal.Task.IsCompleted)
            {
                return;
            }

            // Readers can concurrently replace stale signals; preserve whichever incomplete wait wins.
            TaskCompletionSource<IReadOnlyList<WorkerChannel>> replacement = CreateReadyChannelsSignal();
            if (ReferenceEquals(Interlocked.CompareExchange(ref _readyChannelsAvailable, replacement, signal), signal))
            {
                return;
            }
        }
    }

    private TaskCompletionSource<IReadOnlyList<WorkerChannel>> GetReadyChannelsSignal()
        => Interlocked.CompareExchange(ref _readyChannelsAvailable, null, null);

    private static TaskCompletionSource<IReadOnlyList<WorkerChannel>> CreateReadyChannelsSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private (long HttpCapacity, int LinkedWorkerCount) ComputeHostStateLocked()
    {
        if (_runtimeAdmissionClosed)
        {
            return (0, _physicallyLinkedSlots.Count);
        }

        long httpCapacity = 0;
        foreach (WorkerSlot slot in _slots.Values)
        {
            if (slot.Channel is not { } channel || channel.IsTerminated)
            {
                continue;
            }

            if (channel.ContributesHttpCapacity && channel.MaxHttpConcurrency is { } maxHttpConcurrency)
            {
                httpCapacity += maxHttpConcurrency;
            }
        }

        return (httpCapacity, _physicallyLinkedSlots.Count);
    }

    private void RefreshHostStateLocked()
    {
        (long httpCapacity, int linkedWorkerCount) = ComputeHostStateLocked();
        HostStateSnapshot currentState = _hostStateClock.Current;
        if (currentState.HttpCapacity == httpCapacity &&
            currentState.LinkedWorkerCount == linkedWorkerCount)
        {
            return;
        }

        AdvanceHostStateLocked(httpCapacity, linkedWorkerCount);
    }

    private void SignalHostStateChangedLocked()
    {
        (long httpCapacity, int linkedWorkerCount) = ComputeHostStateLocked();
        AdvanceHostStateLocked(httpCapacity, linkedWorkerCount);
    }

    private void AdvanceHostStateLocked(long httpCapacity, int linkedWorkerCount)
    {
        HostStateSnapshot state = _hostStateClock.Advance(httpCapacity, linkedWorkerCount);
        TaskCompletionSource<long> changed = _hostStateChanged;
        _hostStateChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult(state.Version);
    }

    private async Task DisposeLinkedChannelAsync(
        WorkerSlot slot,
        RpcClientWorkerChannel channel)
    {
        try
        {
            await channel.DisposeAsync();
        }
        finally
        {
            CompletePhysicalUnlink(slot);
        }
    }

    private async Task<Exception> DisposeLinkedChannelAndCaptureExceptionAsync(
        WorkerSlot slot,
        RpcClientWorkerChannel channel,
        Exception exception)
    {
        if (channel is null)
        {
            return exception;
        }

        try
        {
            return await channel.DisposeAndCaptureExceptionAsync(exception);
        }
        finally
        {
            CompletePhysicalUnlink(slot);
        }
    }

    private void CompletePhysicalUnlink(WorkerSlot slot)
    {
        lock (_stateLock)
        {
            if (_physicallyLinkedSlots.Remove(slot))
            {
                SignalHostStateChangedLocked();
            }
        }
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

                if (channel is not null)
                {
                    channel.FailCommittedInvocations(new ObjectDisposedException(channel.GetType().FullName));
                }

                disposalException = await DisposeLinkedChannelAndCaptureExceptionAsync(
                    slot,
                    channel,
                    disposalException);
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
        // The exact active setup context attempted for this channel.
        public ChannelSetupContext AttemptedSetupContext { get; set; }

        public RpcClientWorkerChannel Channel { get; set; }

        // The initial link owns this gate before its slot is published.
        public SemaphoreSlim Gate { get; } = new(0, 1);

        // The irreversible drain shared by repeated selection calls.
        public Task<bool> DrainTask { get; set; }

        // The recorded gRPC endpoint distinguishes exact retries from conflicting requests.
        public Uri Endpoint { get; init; }

        // The link task shared by exact retries; cleared when the accepted channel is detached.
        public Task<WorkerChannel> LinkTask { get; set; }

        // The original failure from the attempted setup context, or null after successful setup.
        public Exception SetupException { get; set; }

        // Indicates that a previously accepted link has terminated and cannot be replayed.
        public bool Terminal { get; set; }
    }

    private sealed record ChannelSetupContext(
        IReadOnlyList<FunctionMetadata> Functions,
        ManagedDependencyOptions ManagedDependencyOptions,
        TimeSpan? FunctionTimeout);

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

        [LoggerMessage(5, LogLevel.Warning, "Failed to configure client-backed worker {WorkerId} for the active ScriptHost.")]
        public static partial void ChannelSetupFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(6, LogLevel.Warning, "Timed out waiting for worker {WorkerId} to close its FunctionRpc link during idle scale-in.")]
        public static partial void GracefulUnlinkTimedOut(ILogger logger, Exception exception, string workerId);
    }
}
