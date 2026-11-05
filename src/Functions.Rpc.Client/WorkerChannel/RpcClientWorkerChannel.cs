// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
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

    private readonly TaskCompletionSource<Exception> _functionLoadFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _invocationAdmissionLock = new();
    private readonly Lock _lifecycleLock = new();
    private readonly TaskCompletionSource _startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _draining;
    private bool _httpCapacityReserved;
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

    internal HostConfiguration HostConfiguration { get; private set; }

    /// <summary>
    /// Gets the assigned group captured after successful initialization, or null when not advertised.
    /// </summary>
    internal string FunctionGroupName { get; private set; }

    /// <summary>
    /// Gets the validated startup HTTP concurrency, or null for a non-HTTP worker.
    /// </summary>
    internal int? MaxHttpConcurrency { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the captured startup assignment belongs to the HTTP function group.
    /// </summary>
    internal bool IsHttpFunctionGroup => string.Equals(FunctionGroupName, FunctionGroups.Http, StringComparison.OrdinalIgnoreCase);

    internal Task<Exception> FunctionLoadFailure => _functionLoadFailure.Task;

    internal bool IsTerminated => Completion.IsCompleted || FunctionLoadFailure.IsCompleted;

    internal bool IsHttpCapacityAvailable
    {
        get
        {
            lock (_invocationAdmissionLock)
            {
                return !_httpCapacityReserved && IsUsableForInvocationsLocked();
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
                return IsUsableForInvocationsLocked();
            }
        }
    }

    internal bool TryReserveHttpScaleIn()
    {
        lock (_invocationAdmissionLock)
        {
            if (_httpCapacityReserved || !IsHttpFunctionGroup || _isAlwaysReady is not true || !IsUsableForInvocationsLocked())
            {
                return false;
            }

            _httpCapacityReserved = true;
            return true;
        }
    }

    internal void ReleaseHttpScaleInReservation()
    {
        lock (_invocationAdmissionLock)
        {
            _httpCapacityReserved = false;
        }
    }

    internal bool TryDispatchInvocation(ScriptInvocationContext context) =>
        TryDispatchInvocation(context, static dispatch => dispatch());

    internal bool TryDispatchInvocation(
        ScriptInvocationContext context,
        Func<Func<bool>, bool> dispatch)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dispatch);

        lock (_invocationAdmissionLock)
        {
            if (!IsUsableForInvocationsLocked())
            {
                return false;
            }

            if (!dispatch(() => TryPostInvocation(context)))
            {
                context.ResultSource.TrySetException(
                    new InvalidOperationException(
                        $"Worker '{Id}' is no longer accepting invocations or is unavailable for attribution."));
            }
        }

        return true;
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

            _draining = true;
            // Scale-in needs every accepted result to become terminal, not necessarily successful.
            return DrainInvocationsAsync().ContinueWith(
                static completed =>
                {
                    _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
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

    protected override void OnWorkerInitRequest(WorkerInitRequest request)
        => request.Capabilities["HostConfiguration"] = "1";

    protected override void OnWorkerInitResponse(WorkerInitResponse response)
    {
        if (response.HostConfiguration is not null && response.Result.Status is not StatusResult.Types.Status.Success)
        {
            throw new WorkerLinkException(WorkerLinkFailureReason.Unavailable,
                "The worker did not report successful initialization for Host configuration delivery.");
        }

        HostConfiguration = response.HostConfiguration?.Clone();
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
            ObjectDisposedException exception = new(GetType().FullName);
            // Metadata callers did not request cancellation; fail so host startup can retry.
            FailPendingMetadataRequest(exception);
            lock (_invocationAdmissionLock)
            {
                _draining = true;
            }

            Shutdown(exception);
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

            // Assignment properties are captured once; later capability updates do not change this channel's identity.
            string functionGroupName = GetCapabilityState(FunctionGroupNameCapability);
            int? maxHttpConcurrency = null;
            if (string.Equals(functionGroupName, FunctionGroups.Http, StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(GetCapabilityState(MaxHttpConcurrencyCapability), NumberStyles.None, CultureInfo.InvariantCulture, out int concurrency)
                    || concurrency is < 1 or > 1000)
                {
                    throw new InvalidOperationException("An HTTP worker must advertise MaxHttpConcurrency as an integer between 1 and 1000.");
                }

                maxHttpConcurrency = concurrency;
            }

            FunctionGroupName = functionGroupName;
            MaxHttpConcurrency = maxHttpConcurrency;
            string isAlwaysReady = GetCapabilityState(IsAlwaysReadyCapability);
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

    private bool IsScaleInEligibleLocked(string functionGroup, bool isAlwaysReady)
        => IsUsableForInvocationsLocked() &&
            string.Equals(FunctionGroupName, functionGroup,
                IsHttpFunctionGroup ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
            _isAlwaysReady == isAlwaysReady;

    private bool IsUsableForInvocationsLocked()
        => !_draining &&
            !IsTerminated &&
            IsChannelReadyForInvocations();
}
