// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
    private readonly Lock _lifecycleLock = new();
    private readonly TaskCompletionSource _startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ILogger _activityLogger;
    private readonly DuplexChannel<StreamingMessage> _transport;
    private readonly Dictionary<string, int> _pendingFunctionLoads = new(StringComparer.Ordinal);
    private LifecycleState _lifecycleState;
    private TimeProvider _activityTimeProvider = TimeProvider.System;
    private long? _idleSince;
    private bool _retiring;

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
        _transport = ownedChannel;
        _activityLogger = logger;
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

    // Unknown metadata is not evidence that an assigned worker is on-demand.
    internal bool? IsAlwaysReady { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the captured startup assignment belongs to the HTTP function group.
    /// </summary>
    internal bool IsHttpFunctionGroup => string.Equals(FunctionGroupName, FunctionGroups.Http, StringComparison.OrdinalIgnoreCase);

    internal Task<Exception> FunctionLoadFailure => _functionLoadFailure.Task;

    internal int OngoingRequests => TrackedInvocationCount;

    internal bool IsTerminated => Completion.IsCompleted || FunctionLoadFailure.IsCompleted;

    // Terminal signals can precede registry cleanup and the base channel's disposal state.
    internal bool IsReadyForInvocations => !Volatile.Read(ref _retiring) && !IsTerminated && IsChannelReadyForInvocations();

    internal void CompleteRequests() => _transport.Writer.TryComplete();

    internal void StartIdleTracking(IReadOnlyList<FunctionMetadata> functions, TimeProvider timeProvider)
    {
        lock (_lifecycleLock)
        {
            _activityTimeProvider = timeProvider;
            // Reactivation can overlap earlier loads; each response completes only one outstanding request.
            foreach (FunctionMetadata function in functions)
            {
                string functionId = function.GetFunctionId();
                _pendingFunctionLoads.TryGetValue(functionId, out int pendingCount);
                _pendingFunctionLoads[functionId] = checked(pendingCount + 1);
            }

            _idleSince = null;
            RecordReadyIdleTime();
        }
    }

    internal bool TryPostInvocation(ScriptInvocationContext context)
    {
        lock (_lifecycleLock)
        {
            if (!IsReadyForInvocations)
            {
                return false;
            }

            PostInvocation(context, trackRequestLifetime: IsHttpFunctionGroup);
            return true;
        }
    }

    internal bool TryClaimIdleRetirement(TimeSpan threshold, out TimeSpan idleDuration)
    {
        lock (_lifecycleLock)
        {
            idleDuration = _idleSince is long since ? _activityTimeProvider.GetElapsedTime(since) : TimeSpan.Zero;
            if (!IsHttpFunctionGroup || IsAlwaysReady is not false || !IsReadyForInvocations ||
                _pendingFunctionLoads.Count != 0 || HasTrackedInvocations || idleDuration <= threshold)
            {
                return false;
            }

            _retiring = true;
            return true;
        }
    }

    internal void StopAcceptingInvocations()
    {
        lock (_lifecycleLock)
        {
            _retiring = true;
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
        IsAlwaysReady = response.Capabilities.TryGetValue(IsAlwaysReadyCapability, out string alwaysReady)
            && bool.TryParse(alwaysReady, out bool parsed) ? parsed : null;
    }

    protected override void OnFunctionLoadError(Exception exception) => _functionLoadFailure.TrySetResult(exception);

    protected override void OnFunctionLoaded(FunctionLoadResponse response)
    {
        lock (_lifecycleLock)
        {
            if (response.Result.Status is StatusResult.Types.Status.Success &&
                _pendingFunctionLoads.TryGetValue(response.FunctionId, out int pendingCount))
            {
                if (pendingCount == 1)
                {
                    _pendingFunctionLoads.Remove(response.FunctionId);
                }
                else
                {
                    _pendingFunctionLoads[response.FunctionId] = pendingCount - 1;
                }

                RecordReadyIdleTime();
            }
        }
    }

    protected override void OnInvocationActivityChanged()
    {
        lock (_lifecycleLock)
        {
            if (HasTrackedInvocations)
            {
                _idleSince = null;
            }
            else
            {
                RecordReadyIdleTime();
            }
        }
    }

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
            if (IsHttpFunctionGroup && IsAlwaysReady is null)
            {
                _activityLogger.LogWarning("Worker {WorkerId} did not advertise valid IsAlwaysReady metadata; idle retirement is disabled for this worker.", Id);
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

    private void RecordReadyIdleTime()
    {
        if (_pendingFunctionLoads.Count == 0 && !HasTrackedInvocations && _idleSince is null)
        {
            _idleSince = _activityTimeProvider.GetTimestamp();
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
}
