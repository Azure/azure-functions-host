// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
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

    private readonly Dictionary<Guid, ScriptInvocationContext> _committedInvocations = [];
    private readonly TaskCompletionSource<Exception> _functionLoadFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _invocationAdmissionLock = new();
    private readonly Lock _lifecycleLock = new();
    private readonly TaskCompletionSource _drainCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _draining;
    private string _functionGroupName;
    private bool? _isAlwaysReady;
    private LifecycleState _lifecycleState;

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

    // Terminal signals can precede registry cleanup and the base channel's disposal state.
    internal bool IsReadyForInvocations
    {
        get
        {
            lock (_invocationAdmissionLock)
            {
                return IsUsableForInvocationsLocked();
            }
        }
    }

    internal void ExecuteUnderInvocationAdmissionLock(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_invocationAdmissionLock)
        {
            action();
        }
    }

    internal bool TryDispatchInvocation(ScriptInvocationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Guid invocationId = context.ExecutionContext.InvocationId;
        lock (_invocationAdmissionLock)
        {
            if (!IsUsableForInvocationsLocked())
            {
                return false;
            }

            string functionId = context.FunctionMetadata.GetFunctionId();
            if (!FunctionInputBuffers.TryGetValue(functionId, out BufferBlock<ScriptInvocationContext> inputBuffer))
            {
                throw new InvalidOperationException(
                    $"Function '{context.FunctionMetadata.Name}' is not loaded by worker '{Id}'.");
            }

            if (!_committedInvocations.TryAdd(invocationId, context))
            {
                throw new InvalidOperationException(
                    $"Invocation '{invocationId}' is already committed to worker '{Id}'.");
            }

            if (!inputBuffer.Post(context))
            {
                _committedInvocations.Remove(invocationId);
                return false;
            }
        }

        _ = context.ResultSource.Task.ContinueWith(
            static (_, state) =>
            {
                (RpcClientWorkerChannel channel, Guid id) = ((RpcClientWorkerChannel, Guid))state;
                channel.ReleaseCommittedInvocation(id);
            },
            (this, invocationId),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return true;
    }

    internal bool TryGetScaleInCandidate(
        string functionGroup,
        bool isAlwaysReady,
        out bool hasCommittedInvocations)
    {
        lock (_invocationAdmissionLock)
        {
            if (!IsScaleInEligibleLocked(functionGroup, isAlwaysReady))
            {
                hasCommittedInvocations = false;
                return false;
            }

            hasCommittedInvocations = _committedInvocations.Count > 0;
            return true;
        }
    }

    internal Task TryBeginScaleIn(
        string functionGroup,
        bool isAlwaysReady,
        bool requireIdle)
    {
        lock (_invocationAdmissionLock)
        {
            if (!IsScaleInEligibleLocked(functionGroup, isAlwaysReady) ||
                (requireIdle && _committedInvocations.Count > 0))
            {
                return null;
            }

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

    protected override void OnMetadataRequestError(Exception exception) => FailPendingMetadataRequest(exception);

    protected override void OnChannelFailure(Exception exception) => FailPendingMetadataRequest(exception);

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

    private void ReleaseCommittedInvocation(Guid invocationId)
    {
        lock (_invocationAdmissionLock)
        {
            _committedInvocations.Remove(invocationId);
            CompleteDrainIfReadyLocked();
        }
    }

    private void CompleteDrainIfReadyLocked()
    {
        if (_draining && _committedInvocations.Count == 0)
        {
            _drainCompletion.TrySetResult();
        }
    }

    private bool IsScaleInEligibleLocked(string functionGroup, bool isAlwaysReady)
        => IsUsableForInvocationsLocked() &&
            string.Equals(_functionGroupName, functionGroup, StringComparison.Ordinal) &&
            _isAlwaysReady == isAlwaysReady;

    private bool IsUsableForInvocationsLocked()
        => !_draining &&
            !IsTerminated &&
            IsChannelReadyForInvocations();
}
