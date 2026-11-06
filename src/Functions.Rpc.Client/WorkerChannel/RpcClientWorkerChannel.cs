// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
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
/// HTTP assignment concurrency counts accepted invocations from buffer admission through worker response handling and HTTP response
/// completion. These slots belong to the channel, not the ScriptHost-scoped dispatcher. HTTP completion is observed without forcing
/// finalization or delaying the invocation result; worker processing and the natural response lifetime must both finish before release.
/// Idle accounting and draining use that same completion boundary; permanent channel termination ends all remaining ownership.
/// </remarks>
internal sealed partial class RpcClientWorkerChannel : WorkerChannel
{
    internal const string FunctionGroupNameCapability = "FunctionGroupName";
    internal const string IsAlwaysReadyCapability = "IsAlwaysReady";
    internal const string MaxHttpConcurrencyCapability = "MaxHttpConcurrency";
    private const string InvocationCompletionProperty = nameof(RpcClientWorkerChannel) + "." + nameof(InvocationCompletion);

    private static readonly TimeSpan InitialIdleThreshold = TimeSpan.FromMinutes(5);
    private readonly TaskCompletionSource<Exception> _functionLoadFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _lifecycleLock = new();
    private readonly ILogger _logger;
    private readonly TaskCompletionSource _startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly RuntimeWorkerCredentials _credentials;
    private readonly IOptionsFactory<RuntimeApplicationAuthenticationOptions> _appAuthenticationOptions;
    private readonly ILogger _activityLogger;
    private IDisposable _ownedHttpProxyService;
    private Uri _httpEndpoint;
    // Initialized only for HTTP workers, using the concurrency limit established during WorkerInit.
    private ConcurrencyLimiter _httpConcurrency;
    private Task _capacityAvailable;
    // Startup transitions are serialized, but admission and eligibility read this state without taking the lifecycle lock.
    private LifecycleState _lifecycleState;
    private TimeProvider _activityTimeProvider = TimeProvider.System;
    private long? _idleSince;
    private int _ongoingRequests;
    private bool _hasReceivedRequest;
    private bool _draining;

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
        RuntimeWorkerCredentials credentials,
        IOptionsFactory<RuntimeApplicationAuthenticationOptions> appAuthenticationOptions)
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
        _credentials = credentials;
        _appAuthenticationOptions = appAuthenticationOptions;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ownedHttpProxyService = httpProxyService as IDisposable;
        Completion = ownedChannel.Reader.Completion;
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
    /// Gets the assigned group captured from the initial successful response, or null before initialization.
    /// </summary>
    internal string FunctionGroupName { get; private set; }

    /// <summary>
    /// Gets the validated startup HTTP concurrency, or null for a non-HTTP worker.
    /// </summary>
    internal int? MaxHttpConcurrency { get; private set; }

    // Assigned WorkerProxy instances advertise this. Null means missing or malformed handshake metadata.
    // Only explicit true exempts a worker from idle retirement.
    internal bool? IsAlwaysReady { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the captured startup assignment belongs to the HTTP function group.
    /// </summary>
    internal bool IsHttpFunctionGroup => string.Equals(FunctionGroupName, FunctionGroups.Http, StringComparison.OrdinalIgnoreCase);

    internal Task<Exception> FunctionLoadFailure => _functionLoadFailure.Task;

    internal Lock LifecycleLock => _lifecycleLock;

    internal int OngoingRequests
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _ongoingRequests;
            }
        }

        set
        {
            lock (_lifecycleLock)
            {
                _ongoingRequests = value;
                if (value > 0)
                {
                    _hasReceivedRequest = true;
                }

                _idleSince = null;
            }
        }
    }

    internal bool IsTerminated => Interlocked.CompareExchange(ref _lifecycleState, default, default) is LifecycleState.Disposed
        || Completion.IsCompleted || FunctionLoadFailure.IsCompleted;

    internal bool IsDraining
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _draining;
            }
        }
    }

    // Terminal signals can precede registry cleanup and the base channel's disposal state.
    internal bool IsReadyForInvocations
    {
        get
        {
            lock (_lifecycleLock)
            {
                return IsReadyForInvocationsLocked();
            }
        }
    }

    internal bool IsHttpCapacityAvailable
    {
        get
        {
            lock (_lifecycleLock)
            {
                return IsReadyForInvocationsLocked();
            }
        }
    }

    internal bool TryStartFunctionMetadataRequest(out Task<List<RawFunctionMetadata>> metadataTask)
    {
        lock (_lifecycleLock)
        {
            if (_draining || IsTerminated)
            {
                metadataTask = null;
                return false;
            }

            try
            {
                metadataTask = GetFunctionMetadata();
            }
            catch (Exception exception) when (!Utility.IsFatal(exception))
            {
                metadataTask = Task.FromException<List<RawFunctionMetadata>>(exception);
            }

            return true;
        }
    }

    internal bool TryDispatchInvocation(ScriptInvocationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_lifecycleLock)
        {
            if (!IsReadyForInvocationsLocked())
            {
                return false;
            }

            if (!TryPostInvocation(context))
            {
                context.ResultSource.TrySetException(
                    new InvalidOperationException($"Worker '{Id}' is no longer accepting invocations."));
            }
        }

        return true;
    }

    internal bool IsScaleInCandidate(string functionGroup, bool isAlwaysReady)
    {
        lock (_lifecycleLock)
        {
            return IsScaleInEligibleLocked(functionGroup, isAlwaysReady);
        }
    }

    internal Task TryBeginScaleIn(string functionGroup, bool isAlwaysReady)
    {
        lock (_lifecycleLock)
        {
            if (!IsScaleInEligibleLocked(functionGroup, isAlwaysReady))
            {
                return null;
            }

            return BeginScaleInLocked();
        }
    }

    internal void StartIdleTracking(TimeProvider timeProvider)
    {
        lock (_lifecycleLock)
        {
            _activityTimeProvider = timeProvider;
            _idleSince = null;
            RecordIdleTime();
        }
    }

    internal bool TryClaimIdleRetirement(TimeSpan threshold, out TimeSpan idleDuration, out Task drainTask)
    {
        lock (_lifecycleLock)
        {
            idleDuration = _idleSince is long since ? _activityTimeProvider.GetElapsedTime(since) : TimeSpan.Zero;
            TimeSpan idleThreshold = _hasReceivedRequest ? threshold : InitialIdleThreshold;
            if (!IsHttpFunctionGroup || IsAlwaysReady is true || !IsReadyForInvocationsLocked() ||
                _ongoingRequests != 0 || idleDuration <= idleThreshold)
            {
                drainTask = null;
                return false;
            }

            drainTask = BeginScaleInLocked();
            return true;
        }
    }

    private Task BeginScaleInLocked()
    {
        _draining = true;
        Task drainTask = DrainInvocationsAsync();
        Task notificationTask = NotifyWorkerProxyDrainingAsync();
        return Task.WhenAll(drainTask, notificationTask);
    }

    internal async Task TerminateWorkerAsync()
    {
        if (string.IsNullOrEmpty(WorkerCapabilities.GetCapabilityState(RpcWorkerConstants.HandlesWorkerTerminateMessage)))
        {
            return;
        }

        int gracePeriodSeconds = WorkerConstants.WorkerTerminateGracePeriodInSeconds;
        try
        {
            await SendStreamingMessageAsync(new()
            {
                WorkerTerminate = new()
                {
                    GracePeriod = Duration.FromTimeSpan(TimeSpan.FromSeconds(gracePeriodSeconds))
                }
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (!Utility.IsFatal(exception))
        {
            LogMessages.WorkerTerminateSendFailed(_logger, exception, Id);
            return;
        }

        Task channelCompletion = OwnedChannelCompletion;
        Task timeout = Task.Delay(TimeSpan.FromSeconds(gracePeriodSeconds));
        if (await Task.WhenAny(channelCompletion, timeout).ConfigureAwait(false) != channelCompletion)
        {
            LogMessages.WorkerTerminateGracePeriodElapsed(_logger, Id, gracePeriodSeconds);
            return;
        }

        try
        {
            await channelCompletion.ConfigureAwait(false);
        }
        catch (Exception exception) when (!Utility.IsFatal(exception))
        {
            LogMessages.WorkerTerminateConnectionClosed(_logger, exception, Id);
        }
    }

    private async Task NotifyWorkerProxyDrainingAsync()
    {
        try
        {
            await SendStreamingMessageAsync(new() { WorkerDraining = new() }).ConfigureAwait(false);
        }
        catch (Exception exception) when (!Utility.IsFatal(exception))
        {
            LogMessages.DrainingNotificationFailed(_logger, exception, Id);
        }
    }

    internal void StopAcceptingInvocations()
    {
        lock (_lifecycleLock)
        {
            _draining = true;
        }
    }

    /// <summary>
    /// Observes available capacity without reserving it. Call after failed admission and retry admission when it completes.
    /// One pending zero-permit acquisition is shared across dispatchers; canceling a caller's wait does not cancel it.
    /// </summary>
    internal Task GetCapacityAvailableTask()
    {
        lock (_lifecycleLock)
        {
            if (IsTerminated || _draining || _httpConcurrency is null)
            {
                return Task.CompletedTask;
            }

            if (_capacityAvailable is null || _capacityAvailable.IsCompleted)
            {
                _capacityAvailable = WaitForCapacityAsync(_httpConcurrency);
            }

            return _capacityAvailable;
        }
    }

    /// <summary>
    /// Reserves a channel-owned slot before posting. Rejection rolls back the lease; accepted work owns it through final completion.
    /// </summary>
    /// <remarks>The limiter and base posting synchronize ownership; lifecycle disposal may race acquisition without serializing admission.</remarks>
    internal bool TryAcceptInvocation(ScriptInvocationContext context, out bool atCapacity)
    {
        ArgumentNullException.ThrowIfNull(context);
        atCapacity = false;

        if (IsTerminated)
        {
            return false;
        }

        ConcurrencyLimiter limiter = Interlocked.CompareExchange(ref _httpConcurrency, null, null);
        if (limiter is null)
        {
            lock (_lifecycleLock)
            {
                return !_draining && !IsTerminated && base.TryPostInvocation(context);
            }
        }

        if (context.Properties.ContainsKey(InvocationCompletionProperty))
        {
            throw new InvalidOperationException("The invocation already owns a worker concurrency lease.");
        }

        RateLimitLease lease;
        try
        {
            lease = limiter.AttemptAcquire(1);
        }
        catch (ObjectDisposedException) when (IsTerminated)
        {
            // Shutdown won the race after the terminal check; no ownership was acquired.
            return false;
        }

        if (!lease.IsAcquired)
        {
            lease.Dispose();
            atCapacity = !IsTerminated;
            return false;
        }

        bool accepted = false;
        try
        {
            HttpRequest request = null;
            bool waitForHttp = context.FunctionMetadata.IsHttpTriggerFunction() && TryGetHttpRequest(context, out request);
            bool trackActivity = IsAlwaysReady is not true;
            InvocationCompletion completion = new(this, context, lease, waitForHttp, trackActivity);
            context.Properties.Add(InvocationCompletionProperty, completion);
            if (waitForHttp)
            {
                // Register before posting: invocation completion may unblock the HTTP pipeline immediately.
                request.HttpContext.Response.OnCompleted(static state => ((InvocationCompletion)state).OnHttpCompletedAsync(), completion);
            }

            // Admission must be atomic with upstream idle-retirement/scale-in claims, but response setup stays outside the lock.
            lock (_lifecycleLock)
            {
                accepted = !_draining && !IsTerminated && base.TryPostInvocation(context);
                if (accepted && trackActivity)
                {
                    OngoingRequests++;
                }
            }
            return accepted;
        }
        finally
        {
            if (!accepted)
            {
                context.Properties.Remove(InvocationCompletionProperty);
                lease.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public override bool TryPostInvocation(ScriptInvocationContext invocationContext)
        => TryAcceptInvocation(invocationContext, out _);

    /// <inheritdoc />
    protected override void OnInvocationCompleted(ScriptInvocationContext context)
    {
        if (context.Properties.TryGetValue(InvocationCompletionProperty, out object value) && value is InvocationCompletion completion)
        {
            completion.OnInvocationCompleted();
        }
        else
        {
            base.OnInvocationCompleted(context);
        }
    }

    /// <summary>
    /// Ends accepted ownership and on-demand activity before returning the lease or completing a drain wait.
    /// </summary>
    private void ReleaseCompletedInvocation(ScriptInvocationContext context, bool trackActivity)
    {
        lock (_lifecycleLock)
        {
            base.OnInvocationCompleted(context);
            if (trackActivity)
            {
                _ongoingRequests--;
                RecordIdleTime();
            }
        }
    }

    /// <inheritdoc />
    protected override Task GetInvocationCompletionTask(ScriptInvocationContext context)
        => context.Properties.TryGetValue(InvocationCompletionProperty, out object value) && value is InvocationCompletion completion
            ? completion.Completion : base.GetInvocationCompletionTask(context);

    /// <inheritdoc />
    protected override void OnInvocationTerminated(ScriptInvocationContext context)
    {
        if (context.Properties.TryGetValue(InvocationCompletionProperty, out object value) && value is InvocationCompletion completion)
        {
            completion.Terminate();
        }
    }

    /// <summary>
    /// Uses the limiter's level-triggered availability observation without consuming a worker slot.
    /// Disposal completes a pending observation with a failed lease; dispatch must resample channel eligibility.
    /// </summary>
    private static async Task WaitForCapacityAsync(ConcurrencyLimiter limiter)
    {
        using RateLimitLease available = await limiter.AcquireAsync(0);
    }

    /// <inheritdoc />
    protected override async Task OnInvocationSendFailureAsync(ScriptInvocationContext context)
    {
        // Forwarding precedes the outbound write. A failed write must not free a slot while HTTP is active.
        if (context.Properties.TryGetValue(ScriptConstants.HttpProxyTask, out object value) && value is Task forwardingTask)
        {
            try
            {
                await forwardingTask;
            }
            catch (Exception exception)
            {
                LogMessages.ForwardingCleanupFailed(_logger, exception);
            }
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
                Interlocked.Exchange(ref _lifecycleState, LifecycleState.Started);

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
        // WorkerProxy finalizes assignment-owned capabilities only for explicit successful initialization.
        if (response.Result is not { Status: StatusResult.Types.Status.Success })
        {
            throw new WorkerLinkException(WorkerLinkFailureReason.Unavailable,
                "The worker did not report successful initialization.");
        }

        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_lifecycleState is LifecycleState.Disposed, this);
            if (FunctionGroupName is not null)
            {
                return;
            }

            if (!response.Capabilities.TryGetValue(FunctionGroupNameCapability, out string functionGroupName)
                || string.IsNullOrWhiteSpace(functionGroupName))
            {
                throw new WorkerLinkException(WorkerLinkFailureReason.Unavailable,
                    "A client-backed worker must advertise its assigned FunctionGroupName.");
            }

            int? maxHttpConcurrency = null;
            if (string.Equals(functionGroupName, FunctionGroups.Http, StringComparison.OrdinalIgnoreCase))
            {
                response.Capabilities.TryGetValue(MaxHttpConcurrencyCapability, out string suppliedConcurrency);
                if (!int.TryParse(suppliedConcurrency, NumberStyles.None, CultureInfo.InvariantCulture, out int concurrency)
                    || concurrency is < 1 or > 1000)
                {
                    throw new WorkerLinkException(WorkerLinkFailureReason.Unavailable,
                        "An HTTP worker must advertise MaxHttpConcurrency as an integer between 1 and 1000.");
                }

                maxHttpConcurrency = concurrency;
            }

            // Read only the original response: capability updates must not reassign an initialized channel.
            HostConfiguration = response.HostConfiguration?.Clone();
            FunctionGroupName = functionGroupName;
            MaxHttpConcurrency = maxHttpConcurrency;
            IsAlwaysReady = response.Capabilities.TryGetValue(IsAlwaysReadyCapability, out string alwaysReady)
                && bool.TryParse(alwaysReady, out bool parsed) ? parsed : null;
            if (maxHttpConcurrency is { } limit)
            {
                Interlocked.Exchange(ref _httpConcurrency, new(new() { PermitLimit = limit, QueueLimit = 0 }));
            }
        }
    }

    protected override void OnFunctionLoadError(Exception exception) => _functionLoadFailure.TrySetResult(exception);

    protected override void OnMetadataRequestError(Exception exception) => FailPendingMetadataRequest(exception);

    /// <inheritdoc />
    protected override void OnChannelFailure(Exception exception)
    {
        StopAdmission();
        if (exception is not null)
        {
            FailPendingMetadataRequest(exception);
        }
    }

    internal override void WorkerInitResponse(StreamingMessage initMessage)
    {
        string proof = RuntimeWorkerAuthentication.RemoveAppProof(initMessage.WorkerInitResponse.Capabilities);
        RuntimeApplicationAuthenticationOptions appAuthentication;
        try
        {
            // Specialization and direct provider updates need not emit reload tokens; do not cache app-key options.
            appAuthentication = _appAuthenticationOptions.Create(Options.DefaultName);
        }
        catch (OptionsValidationException)
        {
            _logger.LogWarning("WorkerProxy application proof authentication failed: {Reason}.", WorkerLinkFailureReason.AuthenticationUnavailable);
            throw new WorkerLinkException(WorkerLinkFailureReason.AuthenticationUnavailable,
                "The specialized application authentication configuration is unavailable.");
        }

        _credentials.ValidateAppProof(proof, appAuthentication.AppName, appAuthentication.AppKey);
        if (initMessage.WorkerInitResponse.Result is not
            { Status: StatusResult.Types.Status.Success or StatusResult.Types.Status.Failure or StatusResult.Types.Status.Cancelled })
        {
            throw new WorkerLinkException(WorkerLinkFailureReason.Unavailable,
                "The worker initialization response must have a known result status.");
        }

        base.WorkerInitResponse(initMessage);
    }

    internal override void UpdateCapabilities(IDictionary<string, string> fields, GrpcCapabilitiesUpdateStrategy strategy)
    {
        if (fields.ContainsKey(RuntimeWorkerAuthentication.AppProofCapability))
        {
            fields = new Dictionary<string, string>(fields, StringComparer.Ordinal);
            RuntimeWorkerAuthentication.RemoveAppProof(fields);
        }

        if (fields.TryGetValue(RpcWorkerConstants.HttpUri, out string advertisedHttpUri)
            && !string.IsNullOrEmpty(advertisedHttpUri))
        {
            if (!Uri.TryCreate(advertisedHttpUri, UriKind.Absolute, out Uri endpoint))
            {
                throw new WorkerLinkException(WorkerLinkFailureReason.Unavailable,
                    "The advertised WorkerProxy HTTP origin is invalid.");
            }

            try
            {
                RpcClientFactory.ValidateEndpoint(endpoint);
            }
            catch (ArgumentException exception)
            {
                throw new WorkerLinkException(WorkerLinkFailureReason.Unavailable,
                    "The advertised WorkerProxy HTTP origin is invalid.", exception);
            }

            if (_httpEndpoint is not null
                && !string.Equals(_httpEndpoint.AbsoluteUri, endpoint.AbsoluteUri, StringComparison.Ordinal))
            {
                throw new WorkerLinkException(WorkerLinkFailureReason.Unavailable,
                    "The initialized WorkerProxy HTTP origin cannot change.");
            }

            _httpEndpoint = endpoint;
        }

        base.UpdateCapabilities(fields, strategy);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        StopAdmission();

        if (disposing)
        {
            ObjectDisposedException exception = new(GetType().FullName);
            // Metadata callers did not request cancellation; fail so host startup can retry.
            FailPendingMetadataRequest(exception);
            StopAcceptingInvocations();
            Shutdown(exception);
        }

        Exception disposalException = null;
        try
        {
            base.Dispose(disposing);
        }
        catch (Exception exception)
        {
            disposalException = exception;
        }

        try
        {
            if (disposing)
            {
                Interlocked.Exchange(ref _ownedHttpProxyService, null)?.Dispose();
            }
        }
        catch (Exception exception)
        {
            disposalException = disposalException is null ? exception : new AggregateException(disposalException, exception);
        }

        if (disposalException is not null)
        {
            ExceptionDispatchInfo.Capture(disposalException).Throw();
        }
    }

    /// <summary>
    /// Makes the channel ineligible before disposing its limiter, waking shared availability waits without allowing readmission.
    /// Active leases remain invocation-owned and can be safely returned after limiter disposal.
    /// </summary>
    private void StopAdmission()
    {
        lock (_lifecycleLock)
        {
            Interlocked.Exchange(ref _lifecycleState, LifecycleState.Disposed);
            _httpConcurrency?.Dispose();
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

            if (IsHttpFunctionGroup && IsAlwaysReady is null)
            {
                _activityLogger.LogWarning("Worker {WorkerId} did not advertise valid IsAlwaysReady metadata; treating this worker as on-demand for idle retirement.", Id);
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

    private void RecordIdleTime()
    {
        if (_ongoingRequests == 0 && _idleSince is null)
        {
            _idleSince = _activityTimeProvider.GetTimestamp();
        }
    }

    private bool IsScaleInEligibleLocked(string functionGroup, bool isAlwaysReady)
        => IsReadyForInvocationsLocked() &&
            string.Equals(FunctionGroupName, functionGroup,
                IsHttpFunctionGroup ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
            IsAlwaysReady == isAlwaysReady;

    private bool IsReadyForInvocationsLocked() => !_draining && !IsTerminated && IsChannelReadyForInvocations();

    private static TimeSpan ValidateStartStreamTimeout(TimeSpan startStreamTimeout)
    {
        if (startStreamTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(startStreamTimeout), "The StartStream timeout must be greater than zero.");
        }

        return startStreamTimeout;
    }

    private static partial class LogMessages
    {
        [LoggerMessage(1000, LogLevel.Warning, "Failed to notify WorkerProxy that worker {WorkerId} is draining.")]
        public static partial void DrainingNotificationFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(1001, LogLevel.Warning, "Failed to send WorkerTerminate to worker {WorkerId}; closing the Host channel.")]
        public static partial void WorkerTerminateSendFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(1002, LogLevel.Warning, "Worker {WorkerId} did not disconnect within {GracePeriodSeconds} seconds after WorkerTerminate; closing the Host channel.")]
        public static partial void WorkerTerminateGracePeriodElapsed(ILogger logger, string workerId, int gracePeriodSeconds);

        [LoggerMessage(1003, LogLevel.Debug, "Worker {WorkerId} disconnected while handling WorkerTerminate.")]
        public static partial void WorkerTerminateConnectionClosed(ILogger logger, Exception exception, string workerId);
    }

    /// <summary>
    /// Joins worker completion with the owned server response lifetime and returns the concurrency lease exactly once.
    /// Manual HTTP invocations and webhook functions without a server response need only worker completion.
    /// </summary>
    private sealed class InvocationCompletion(
        RpcClientWorkerChannel channel, ScriptInvocationContext context, RateLimitLease lease, bool waitForHttp, bool trackActivity)
    {
        private const int HttpCompleted = 1;
        private const int InvocationCompleted = 2;
        private readonly ScriptInvocationContext _context = context;
        private readonly RpcClientWorkerChannel _channel = channel;
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private RateLimitLease _lease = lease;
        private int _completed = waitForHttp ? 0 : HttpCompleted;

        /// <summary>
        /// Gets the end of accepted ownership, independent of when the invocation result is delivered.
        /// </summary>
        internal Task Completion => _completion.Task;

        /// <summary>
        /// Observes natural response completion without forcing finalization or delaying the invocation result.
        /// </summary>
        internal Task OnHttpCompletedAsync()
        {
            if (Interlocked.Or(ref _completed, HttpCompleted) == InvocationCompleted)
            {
                Release();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Releases only when both worker processing and any owned response lifetime have ended.
        /// </summary>
        internal void OnInvocationCompleted()
        {
            if (Interlocked.Or(ref _completed, InvocationCompleted) == HttpCompleted)
            {
                Release();
            }
        }

        /// <summary>
        /// Ends ownership unconditionally when the worker channel is permanently terminated.
        /// </summary>
        internal void Terminate() => Release();

        /// <summary>
        /// Ends lifecycle ownership before publishing capacity, returning the permit even if lifecycle cleanup fails.
        /// </summary>
        private void Release()
        {
            RateLimitLease lease = Interlocked.Exchange(ref _lease, null);
            if (lease is null)
            {
                return;
            }

            try
            {
                _channel.ReleaseCompletedInvocation(_context, trackActivity);
            }
            finally
            {
                try
                {
                    lease.Dispose();
                }
                finally
                {
                    _completion.TrySetResult();
                }
            }
        }
    }

    private static partial class LogMessages
    {
        [LoggerMessage(0, LogLevel.Error, "HTTP forwarding failed while cleaning up an invocation that could not be sent.")]
        public static partial void ForwardingCleanupFailed(ILogger logger, Exception exception);
    }
}
