// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Runs the shared worker protocol over a client-owned FunctionRpc channel.
/// </summary>
/// <remarks>
/// Concurrent and repeated starts share one initialization attempt. A token already canceled when <see cref="StartAsync"/> is called does not
/// begin initialization. Once initialization begins, cancellation only stops the individual caller's wait; shared initialization continues
/// until it succeeds, fails, times out, or the channel is disposed.
/// </remarks>
internal sealed class RpcClientWorkerChannel : WorkerChannel
{
    internal const string FunctionGroupNameCapability = "FunctionGroupName";
    internal const string IsAlwaysReadyCapability = "IsAlwaysReady";
    internal const string MaxHttpConcurrencyCapability = "MaxHttpConcurrency";
    internal const string WorkerAssignmentMetadataVersionCapability = "WorkerAssignmentMetadataVersion";
    internal const string WorkerAssignmentMetadataVersion = "1";

    private readonly Dictionary<Guid, InvocationPermit> _committedInvocations = [];
    private readonly TaskCompletionSource<Exception> _functionLoadFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _invocationAdmissionLock = new();
    private readonly Lock _lifecycleLock = new();
    private readonly Dictionary<Guid, ScriptInvocationContext> _pendingInvocations = [];
    private readonly HashSet<string> _pendingScaleInFunctionLoads = new(StringComparer.OrdinalIgnoreCase);
    private readonly ChannelWriter<StreamingMessage> _requestWriter;
    private readonly TaskCompletionSource _drainCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _draining;
    private string _functionGroupName;
    private TaskCompletionSource<long> _hostCapacityChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _hostCapacityVersion;
    private bool _invocationAdmissionEnabled = true;
    private bool? _isAlwaysReady;
    private LifecycleState _lifecycleState;
    private int? _maxHttpConcurrency;
    private bool _scaleInFunctionLoadFailed;
    private bool _scaleInFunctionLoadsTracked;
    private bool _scaleInReady;
    private Guid? _scaleInReservationId;
    private bool _terminalAdmissionClosed;

    public RpcClientWorkerChannel(
        string workerId,
        DuplexChannel<StreamingMessage> ownedChannel,
        IScriptEventManager eventManager,
        IScriptHostManager hostManager,
        RpcWorkerConfig workerConfig,
        ILogger logger,
        IMetricsLogger metricsLogger,
        int attemptCount,
        IEnvironment environment,
        IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
        ISharedMemoryManager sharedMemoryManager,
        IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions,
        IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
        IAppCapabilitiesStore appCapabilitiesStore,
        IHttpProxyService httpProxyService)
        : base(
            workerId,
            ownedChannel,
            eventManager,
            hostManager,
            workerConfig,
            logger,
            metricsLogger,
            attemptCount,
            environment,
            applicationHostOptions,
            sharedMemoryManager,
            workerConcurrencyOptions,
            hostingConfigOptions,
            appCapabilitiesStore,
            httpProxyService)
    {
        _requestWriter = ownedChannel.Writer;
        Completion = ownedChannel.Reader.Completion;
    }

    private enum LifecycleState
    {
        NotStarted,
        Started,
        Disposed,
    }

    internal Task Completion { get; }

    internal Task<Exception> FunctionLoadFailure => _functionLoadFailure.Task;

    internal bool IsTerminated => Completion.IsCompleted || FunctionLoadFailure.IsCompleted;

    internal bool ContributesHttpCapacity
    {
        get
        {
            lock (_invocationAdmissionLock)
            {
                return IsHttpFunctionGroup &&
                    _maxHttpConcurrency is > 0 &&
                    _scaleInReady &&
                    CanAdmitInvocationLocked();
            }
        }
    }

    internal int? MaxHttpConcurrency => _maxHttpConcurrency;

    internal long HostCapacityVersion
    {
        get
        {
            lock (_invocationAdmissionLock)
            {
                return _hostCapacityVersion;
            }
        }
    }

    internal bool ScaleInFunctionLoadsComplete
    {
        get
        {
            lock (_invocationAdmissionLock)
            {
                return _scaleInFunctionLoadsTracked && _pendingScaleInFunctionLoads.Count == 0;
            }
        }
    }

    // Terminal signals can precede registry cleanup and the base channel's disposal state.
    internal bool IsReadyForInvocations
    {
        get
        {
            lock (_invocationAdmissionLock)
            {
                return CanAdmitInvocationLocked();
            }
        }
    }

    internal bool IsHttpFunctionGroup
    {
        get
        {
            lock (_invocationAdmissionLock)
            {
                return string.Equals(
                    _functionGroupName,
                    FunctionGroups.Http,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    internal void CompleteRequests() => _requestWriter.TryComplete();

    internal bool TryDispatchInvocation(ScriptInvocationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        lock (_invocationAdmissionLock)
        {
            if (!CanAdmitInvocationLocked())
            {
                return false;
            }

            string functionId = context.FunctionMetadata.GetFunctionId();
            if (!FunctionInputBuffers.TryGetValue(
                functionId,
                out BufferBlock<ScriptInvocationContext> inputBuffer))
            {
                throw new InvalidOperationException(
                    $"Function '{context.FunctionMetadata.Name}' is not loaded by worker '{Id}'.");
            }

            Guid invocationId = context.ExecutionContext.InvocationId;
            if (_pendingInvocations.ContainsKey(invocationId) ||
                _committedInvocations.ContainsKey(invocationId))
            {
                throw new InvalidOperationException(
                    $"Invocation '{invocationId}' is already owned by worker '{Id}'.");
            }

            if (!inputBuffer.Post(context))
            {
                return false;
            }

            _pendingInvocations.Add(invocationId, context);
            return true;
        }
    }

    internal bool IsScaleInCandidate(string functionGroup, bool isAlwaysReady)
    {
        lock (_invocationAdmissionLock)
        {
            return IsScaleInEligibleLocked(functionGroup, isAlwaysReady);
        }
    }

    internal Task TryBeginScaleIn(string functionGroup, bool isAlwaysReady)
    {
        lock (_invocationAdmissionLock)
        {
            if (!IsScaleInEligibleLocked(functionGroup, isAlwaysReady))
            {
                return null;
            }

            _scaleInReservationId = null;
            _draining = true;
            CompleteDrainIfReadyLocked();
            return _drainCompletion.Task;
        }
    }

    /// <summary>
    /// Starts inbound protocol processing and waits for the worker initialization handshake.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels this caller's wait without canceling shared initialization.</param>
    /// <returns>A task that completes after a successful WorkerInitResponse, is canceled for this caller, or faults when initialization cannot complete.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_lifecycleState is LifecycleState.Disposed, this);

            if (_lifecycleState is LifecycleState.NotStarted)
            {
                _lifecycleState = LifecycleState.Started;

                try
                {
                    MarkWorkerInitializing();
                    BeginInboundProcessing(ValidateStartStreamTimeout(WorkerConfig.CountOptions.ProcessStartupTimeout));
                    _ = CompleteStartAsync();
                }
                catch (Exception exception)
                {
                    _startCompletion.TrySetException(exception);
                }
            }
        }

        return _startCompletion.Task.WaitAsync(cancellationToken);
    }

    protected override void OnFunctionLoadError(Exception exception) => _functionLoadFailure.TrySetResult(exception);

    protected override void OnFunctionLoadResponse(FunctionLoadResponse loadResponse)
    {
        lock (_invocationAdmissionLock)
        {
            if (!_scaleInFunctionLoadsTracked ||
                !_pendingScaleInFunctionLoads.Remove(loadResponse.FunctionId))
            {
                return;
            }

            if (loadResponse.Result.Status is not StatusResult.Types.Status.Success)
            {
                _scaleInFunctionLoadFailed = true;
            }

            if (UpdateScaleInReadinessLocked())
            {
                SignalHostCapacityChangedLocked();
            }
        }
    }

    protected override void OnMetadataRequestError(Exception exception) => FailPendingMetadataRequest(exception);

    protected override void OnChannelFailure(Exception exception) => FailPendingMetadataRequest(exception);

    protected override bool TryBeginInvocationDispatch(
        ScriptInvocationContext context,
        bool usesHttpProxy)
    {
        InvocationPermit permit = null;
        lock (_invocationAdmissionLock)
        {
            Guid invocationId = context.ExecutionContext.InvocationId;
            if (_pendingInvocations.Remove(
                    invocationId,
                    out ScriptInvocationContext pendingInvocation) &&
                ReferenceEquals(pendingInvocation, context) &&
                !_terminalAdmissionClosed)
            {
                permit = new(this, context, usesHttpProxy);
                if (!_committedInvocations.TryAdd(
                    invocationId,
                    permit))
                {
                    throw new InvalidOperationException(
                        $"Invocation '{invocationId}' is already committed to worker '{Id}'.");
                }
            }
        }

        if (permit is null)
        {
            context.ResultSource.TrySetException(
                new InvalidOperationException($"Worker '{Id}' no longer accepts new invocations."));
            return false;
        }

        permit.ObserveResult(context.ResultSource.Task);
        return true;
    }

    protected override bool TryBeginInvocationSend(ScriptInvocationContext context)
    {
        lock (_invocationAdmissionLock)
        {
            return !_terminalAdmissionClosed &&
                _committedInvocations.ContainsKey(context.ExecutionContext.InvocationId);
        }
    }

    protected override void OnInvocationDispatchCompleted(
        ScriptInvocationContext context,
        Task httpForwardingTask)
    {
        InvocationPermit permit;
        lock (_invocationAdmissionLock)
        {
            _committedInvocations.TryGetValue(
                context.ExecutionContext.InvocationId,
                out permit);
        }

        permit?.ObserveHttpForwarding(httpForwardingTask);
    }

    internal bool CancelScaleInReservation(Guid reservationId)
    {
        lock (_invocationAdmissionLock)
        {
            if (_scaleInReservationId != reservationId || _draining || _terminalAdmissionClosed)
            {
                return false;
            }

            _scaleInReservationId = null;
            return true;
        }
    }

    internal Task CommitScaleInReservation(Guid reservationId)
    {
        lock (_invocationAdmissionLock)
        {
            if (_scaleInReservationId != reservationId || _terminalAdmissionClosed)
            {
                return null;
            }

            _scaleInReservationId = null;
            _draining = true;
            CompleteDrainIfReadyLocked();
            return _drainCompletion.Task;
        }
    }

    internal Task BeginShutdownDrain()
    {
        lock (_invocationAdmissionLock)
        {
            _invocationAdmissionEnabled = false;
            _scaleInReady = false;
            _scaleInReservationId = null;
            _draining = true;
            CompleteDrainIfReadyLocked();
            return _drainCompletion.Task;
        }
    }

    internal Task<long> WaitForHostCapacityChangeAsync(
        long version,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_invocationAdmissionLock)
        {
            if (_hostCapacityVersion != version)
            {
                return Task.FromResult(_hostCapacityVersion);
            }

            return _hostCapacityChanged.Task.WaitAsync(cancellationToken);
        }
    }

    internal void FailCommittedInvocations(Exception exception)
    {
        InvocationPermit[] failures;
        ScriptInvocationContext[] pendingInvocations;
        lock (_invocationAdmissionLock)
        {
            _terminalAdmissionClosed = true;
            _scaleInReservationId = null;
            _draining = true;
            failures = [.. _committedInvocations.Values];
            pendingInvocations = WithdrawPendingInvocationsLocked();
            CompleteDrainIfReadyLocked();
        }

        Exception shutdownException = exception;
        if (exception is null || exception is FunctionTimeoutException)
        {
            string suffix = exception?.Message is not null ? " Reason: " + exception.Message : string.Empty;
            shutdownException = new FunctionTimeoutAbortException(
                "Worker channel is shutting down. Aborting function." + suffix,
                exception);
        }

        foreach (ScriptInvocationContext invocation in pendingInvocations)
        {
            invocation.ResultSource.TrySetException(shutdownException);
        }

        foreach (InvocationPermit permit in failures)
        {
            permit.Fail(shutdownException);
        }
    }

    internal void SetScaleInReady(bool ready)
    {
        lock (_invocationAdmissionLock)
        {
            _invocationAdmissionEnabled = ready;
            if (!ready)
            {
                _scaleInReady = false;
                _scaleInReservationId = null;
            }
            else
            {
                UpdateScaleInReadinessLocked();
            }
        }
    }

    internal void TrackScaleInFunctionLoads(
        IEnumerable<Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata> functions)
    {
        ArgumentNullException.ThrowIfNull(functions);

        lock (_invocationAdmissionLock)
        {
            _pendingScaleInFunctionLoads.Clear();
            foreach (Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata function in functions)
            {
                _pendingScaleInFunctionLoads.Add(function.GetFunctionId());
            }

            _scaleInFunctionLoadFailed = false;
            _scaleInFunctionLoadsTracked = true;
            _scaleInReady = false;
            _scaleInReservationId = null;
        }
    }

    internal bool TryReserveScaleIn(out Guid reservationId)
    {
        lock (_invocationAdmissionLock)
        {
            if (!IsHttpFunctionGroup ||
                _isAlwaysReady is not false ||
                _maxHttpConcurrency is not > 0 ||
                !_scaleInReady ||
                !CanAdmitInvocationLocked() ||
                _pendingInvocations.Count != 0 ||
                _committedInvocations.Count != 0)
            {
                reservationId = default;
                return false;
            }

            reservationId = Guid.NewGuid();
            _scaleInReservationId = reservationId;
            return true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        lock (_lifecycleLock)
        {
            _lifecycleState = LifecycleState.Disposed;
        }

        if (disposing)
        {
            // Metadata callers did not request cancellation; fail so host startup can retry.
            FailPendingMetadataRequest(new ObjectDisposedException(GetType().FullName));
        }

        base.Dispose(disposing);
    }

    internal Task BeginRuntimeDrain()
    {
        ScriptInvocationContext[] pendingInvocations;
        lock (_invocationAdmissionLock)
        {
            Volatile.Write(ref _draining, true);
            pendingInvocations = WithdrawPendingInvocationsLocked();
            CompleteDrainIfReadyLocked();
        }

        foreach (ScriptInvocationContext invocation in pendingInvocations)
        {
            invocation.ResultSource.TrySetException(
                new InvalidOperationException($"Worker '{Id}' was selected for Runtime drain before the invocation committed."));
        }

        return _drainCompletion.Task;
    }

    internal void ExpireRuntimeDrain(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        InvocationPermit[] committedInvocations;
        lock (_invocationAdmissionLock)
        {
            Volatile.Write(ref _draining, true);
            Volatile.Write(ref _terminalAdmissionClosed, true);
            committedInvocations = [.. _committedInvocations.Values];
        }

        foreach (InvocationPermit invocation in committedInvocations)
        {
            invocation.Expire(exception);
        }
    }

    private async Task CompleteStartAsync()
    {
        try
        {
            if (!await WorkerInitialization.ConfigureAwait(false))
            {
                throw new InvalidOperationException("The worker reported unsuccessful initialization.");
            }

            bool trustedAssignmentMetadata = string.Equals(
                GetWorkerCapabilityState(WorkerAssignmentMetadataVersionCapability),
                WorkerAssignmentMetadataVersion,
                StringComparison.Ordinal);
            if (trustedAssignmentMetadata)
            {
                _functionGroupName = GetWorkerCapabilityState(FunctionGroupNameCapability);
                string isAlwaysReady = GetWorkerCapabilityState(IsAlwaysReadyCapability);
                _isAlwaysReady = bool.TryParse(isAlwaysReady, out bool parsedIsAlwaysReady)
                    ? parsedIsAlwaysReady
                    : null;
                string maxHttpConcurrency = GetWorkerCapabilityState(MaxHttpConcurrencyCapability);
                _maxHttpConcurrency =
                    int.TryParse(maxHttpConcurrency, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedMaxHttpConcurrency) &&
                    parsedMaxHttpConcurrency > 0
                        ? parsedMaxHttpConcurrency
                        : null;
            }

            _startCompletion.TrySetResult();
        }
        catch (OperationCanceledException exception)
        {
            _startCompletion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            _startCompletion.TrySetException(exception);
        }
    }

    private static TimeSpan ValidateStartStreamTimeout(TimeSpan startStreamTimeout)
    {
        if (startStreamTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(startStreamTimeout), "The StartStream timeout must be greater than zero.");
        }

        return startStreamTimeout;
    }

    private bool IsScaleInEligibleLocked(string functionGroup, bool isAlwaysReady)
        => HasTrustedAssignmentMetadataLocked() &&
            CanAdmitInvocationLocked() &&
            string.Equals(_functionGroupName, functionGroup, StringComparison.Ordinal) &&
            _isAlwaysReady == isAlwaysReady;

    private bool CanAdmitInvocationLocked()
        => _invocationAdmissionEnabled &&
            !_draining &&
            _scaleInReservationId is null &&
            !_terminalAdmissionClosed &&
            !IsTerminated &&
            IsChannelReadyForInvocations();

    private bool HasTrustedAssignmentMetadataLocked()
        => !string.IsNullOrWhiteSpace(_functionGroupName) &&
            _isAlwaysReady.HasValue;

    private bool HasTrustedScaleInMetadataLocked()
        => HasTrustedAssignmentMetadataLocked() &&
            IsHttpFunctionGroup &&
            _isAlwaysReady is false &&
            _maxHttpConcurrency is > 0;

    private void CompleteDrainIfReadyLocked()
    {
        if (_draining &&
            _pendingInvocations.Count == 0 &&
            _committedInvocations.Count == 0)
        {
            _drainCompletion.TrySetResult();
        }
    }

    private ScriptInvocationContext[] WithdrawPendingInvocationsLocked()
    {
        List<ScriptInvocationContext> pendingInvocations =
            [.. _pendingInvocations.Values];
        HashSet<Guid> invocationIds = [];
        foreach (ScriptInvocationContext invocation in pendingInvocations)
        {
            invocationIds.Add(invocation.ExecutionContext.InvocationId);
        }

        _pendingInvocations.Clear();
        foreach (BufferBlock<ScriptInvocationContext> inputBuffer in FunctionInputBuffers.Values)
        {
            while (inputBuffer.TryReceive(out ScriptInvocationContext invocation))
            {
                if (invocationIds.Add(invocation.ExecutionContext.InvocationId))
                {
                    pendingInvocations.Add(invocation);
                }
            }
        }

        return [.. pendingInvocations];
    }

    private void ReleaseInvocation(Guid invocationId, InvocationPermit permit)
    {
        lock (_invocationAdmissionLock)
        {
            if (_committedInvocations.TryGetValue(invocationId, out InvocationPermit current) &&
                ReferenceEquals(current, permit))
            {
                _committedInvocations.Remove(invocationId);
                CompleteDrainIfReadyLocked();
            }
        }
    }

    private bool UpdateScaleInReadinessLocked()
    {
        bool ready = _invocationAdmissionEnabled &&
            (!_scaleInFunctionLoadsTracked ||
                (!_scaleInFunctionLoadFailed && _pendingScaleInFunctionLoads.Count == 0));
        bool changed = _scaleInReady != ready;
        _scaleInReady = ready;
        if (!ready)
        {
            _scaleInReservationId = null;
        }

        return changed;
    }

    private void SignalHostCapacityChangedLocked()
    {
        long version = ++_hostCapacityVersion;
        TaskCompletionSource<long> changed = _hostCapacityChanged;
        _hostCapacityChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult(version);
    }

    private sealed class InvocationPermit : IDisposable
    {
        private readonly ScriptInvocationContext _context;
        private readonly Guid _invocationId;
        private readonly RpcClientWorkerChannel _owner;
        private int _httpCompletionObserved;
        private int _released;
        private int _remainingCompletions;

        public InvocationPermit(
            RpcClientWorkerChannel owner,
            ScriptInvocationContext context,
            bool usesHttpProxy)
        {
            _owner = owner;
            _context = context;
            _invocationId = context.ExecutionContext.InvocationId;
            _remainingCompletions = usesHttpProxy ? 2 : 1;
            RequiresHttpCompletion = usesHttpProxy;
        }

        private bool RequiresHttpCompletion { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.ReleaseInvocation(_invocationId, this);
            }
        }

        public void Expire(Exception exception)
        {
            _context.ResultSource.TrySetException(exception);
            Dispose();
        }

        public void Fail(Exception exception) => Expire(exception);

        public void ObserveHttpForwarding(Task forwardingTask)
        {
            if (!RequiresHttpCompletion ||
                Interlocked.Exchange(ref _httpCompletionObserved, 1) != 0)
            {
                return;
            }

            if (forwardingTask is null)
            {
                Complete();
                return;
            }

            _ = forwardingTask.ContinueWith(
                static (_, state) => ((InvocationPermit)state!).Complete(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public void ObserveResult(Task resultTask)
        {
            _ = resultTask.ContinueWith(
                static (_, state) => ((InvocationPermit)state!).Complete(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void Complete()
        {
            if (Volatile.Read(ref _released) == 0 &&
                Interlocked.Decrement(ref _remainingCompletions) == 0)
            {
                Dispose();
            }
        }
    }
}
