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

    private readonly Dictionary<Guid, InvocationPermit> _committedInvocations = [];
    private readonly TaskCompletionSource<Exception> _functionLoadFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _invocationAdmissionLock = new();
    private readonly Lock _lifecycleLock = new();
    private readonly HashSet<string> _pendingScaleInFunctionLoads = new(StringComparer.OrdinalIgnoreCase);
    private readonly ChannelWriter<StreamingMessage> _requestWriter;
    private readonly TaskCompletionSource _drainCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _timeProvider;
    private bool _draining;
    private string _functionGroupName;
    private long? _idleSinceTimestamp;
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
        IHttpProxyService httpProxyService,
        TimeProvider timeProvider = null)
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
        _timeProvider = timeProvider ?? TimeProvider.System;
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

    private bool IsHttpFunctionGroup =>
        string.Equals(_functionGroupName, FunctionGroups.Http, StringComparison.OrdinalIgnoreCase);

    internal void CompleteRequests() => _requestWriter.TryComplete();

    internal bool TryDispatchInvocation(ScriptInvocationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InvocationPermit permit = null;
        try
        {
            lock (_invocationAdmissionLock)
            {
                if (!CanAdmitInvocationLocked())
                {
                    return false;
                }

                string functionId = context.FunctionMetadata.GetFunctionId();
                if (!FunctionInputBuffers.TryGetValue(functionId, out BufferBlock<ScriptInvocationContext> inputBuffer))
                {
                    throw new InvalidOperationException(
                        $"Function '{context.FunctionMetadata.Name}' is not loaded by worker '{Id}'.");
                }

                if (!HasTrustedScaleInMetadataLocked())
                {
                    return inputBuffer.Post(context);
                }

                Guid invocationId = context.ExecutionContext.InvocationId;
                permit = new(this, context);
                if (!_committedInvocations.TryAdd(invocationId, permit))
                {
                    throw new InvalidOperationException(
                        $"Invocation '{invocationId}' is already committed to worker '{Id}'.");
                }

                if (!inputBuffer.Post(context))
                {
                    return false;
                }

                _idleSinceTimestamp = null;
            }

            permit.ObserveResult(context.ResultSource.Task);
            permit = null;

            return true;
        }
        finally
        {
            permit?.Dispose();
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

            UpdateScaleInReadinessLocked();
        }
    }

    protected override void OnMetadataRequestError(Exception exception) => FailPendingMetadataRequest(exception);

    protected override void OnChannelFailure(Exception exception) => FailPendingMetadataRequest(exception);

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

    internal void FailCommittedInvocations(Exception exception)
    {
        InvocationPermit[] failures;
        lock (_invocationAdmissionLock)
        {
            _terminalAdmissionClosed = true;
            _scaleInReservationId = null;
            _draining = true;
            failures = [.. _committedInvocations.Values];
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
                _idleSinceTimestamp = null;
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
            _idleSinceTimestamp = null;
        }
    }

    internal bool TryReserveIdleScaleIn(TimeSpan minimumIdleDuration, out Guid reservationId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumIdleDuration, TimeSpan.Zero);

        lock (_invocationAdmissionLock)
        {
            long now = _timeProvider.GetTimestamp();
            if (!IsHttpFunctionGroup ||
                _isAlwaysReady is not false ||
                _maxHttpConcurrency is not > 0 ||
                !_scaleInReady ||
                !CanAdmitInvocationLocked() ||
                _committedInvocations.Count != 0 ||
                _idleSinceTimestamp is not { } idleSince ||
                _timeProvider.GetElapsedTime(idleSince, now) < minimumIdleDuration)
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

    private async Task CompleteStartAsync()
    {
        try
        {
            if (!await WorkerInitialization.ConfigureAwait(false))
            {
                throw new InvalidOperationException("The worker reported unsuccessful initialization.");
            }

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

    private bool CanAdmitInvocationLocked()
        => _invocationAdmissionEnabled &&
            !_draining &&
            _scaleInReservationId is null &&
            !_terminalAdmissionClosed &&
            !IsTerminated &&
            IsChannelReadyForInvocations();

    private bool HasTrustedScaleInMetadataLocked()
        => IsHttpFunctionGroup &&
            _isAlwaysReady is false &&
            _maxHttpConcurrency is > 0;

    private void CompleteDrainIfReadyLocked()
    {
        if (_draining && _committedInvocations.Count == 0)
        {
            _drainCompletion.TrySetResult();
        }
    }

    private void ReleaseInvocation(Guid invocationId, InvocationPermit permit)
    {
        lock (_invocationAdmissionLock)
        {
            if (_committedInvocations.TryGetValue(invocationId, out InvocationPermit current) &&
                ReferenceEquals(current, permit))
            {
                _committedInvocations.Remove(invocationId);
                if (_committedInvocations.Count == 0 &&
                    _scaleInReady &&
                    !_draining &&
                    _scaleInReservationId is null &&
                    !_terminalAdmissionClosed)
                {
                    _idleSinceTimestamp ??= _timeProvider.GetTimestamp();
                }

                CompleteDrainIfReadyLocked();
            }
        }
    }

    private void UpdateScaleInReadinessLocked()
    {
        bool ready = _invocationAdmissionEnabled &&
            (!_scaleInFunctionLoadsTracked ||
                (!_scaleInFunctionLoadFailed && _pendingScaleInFunctionLoads.Count == 0));
        _scaleInReady = ready;
        if (!ready)
        {
            _scaleInReservationId = null;
            _idleSinceTimestamp = null;
            return;
        }

        if (_committedInvocations.Count == 0 &&
            !_draining &&
            _scaleInReservationId is null &&
            !_terminalAdmissionClosed)
        {
            _idleSinceTimestamp ??= _timeProvider.GetTimestamp();
        }
    }

    private sealed class InvocationPermit : IDisposable
    {
        private readonly ScriptInvocationContext _context;
        private readonly Guid _invocationId;
        private readonly RpcClientWorkerChannel _owner;
        private int _released;

        public InvocationPermit(RpcClientWorkerChannel owner, ScriptInvocationContext context)
        {
            _owner = owner;
            _context = context;
            _invocationId = context.ExecutionContext.InvocationId;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.ReleaseInvocation(_invocationId, this);
            }
        }

        public void Fail(Exception exception) => _context.ResultSource.TrySetException(exception);

        public void ObserveResult(Task resultTask)
        {
            _ = resultTask.ContinueWith(
                static (_, state) => ((InvocationPermit)state).Dispose(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
