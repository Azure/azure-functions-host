// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
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
/// </remarks>
internal sealed partial class RpcClientWorkerChannel : WorkerChannel
{
    internal const string FunctionGroupNameCapability = "FunctionGroupName";
    internal const string IsAlwaysReadyCapability = "IsAlwaysReady";
    internal const string MaxHttpConcurrencyCapability = "MaxHttpConcurrency";

    private static readonly TimeSpan InitialIdleThreshold = TimeSpan.FromMinutes(5);
    private readonly TaskCompletionSource<Exception> _functionLoadFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _lifecycleLock = new();
    private readonly ILogger _payloadLogger;
    private readonly CancellationTokenSource _payloadShutdown = new();
    private readonly TaskCompletionSource _startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly RuntimeWorkerCredentials _credentials;
    private readonly IOptionsFactory<RuntimeApplicationAuthenticationOptions> _appAuthenticationOptions;
    private readonly ILogger _logger;
    private readonly ILogger _activityLogger;
    private IDisposable _ownedHttpProxyService;
    private Uri _httpEndpoint;
    private ExtensionPayloadReceiver _extensionPayloadReceiver;
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
        _logger = logger;
        _ownedHttpProxyService = httpProxyService as IDisposable;
        Completion = ownedChannel.Reader.Completion;
        _activityLogger = logger;
        _payloadLogger = logger;
    }

    private enum LifecycleState
    {
        NotStarted,
        Started,
        Disposed,
    }

    internal Task Completion { get; }

    internal HostConfiguration HostConfiguration { get; private set; }

    internal string ExtensionPayloadCapability => GetCapabilityState(WorkerExtensionPayloadManager.CapabilityName);

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

    internal bool IsTerminated => Completion.IsCompleted || FunctionLoadFailure.IsCompleted;

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
        // WorkerProxy finalizes assignment-owned capabilities only for explicit successful initialization.
        if (response.Result is not { Status: StatusResult.Types.Status.Success })
        {
            throw new WorkerLinkException(WorkerLinkFailureReason.Unavailable,
                "The worker did not report successful initialization.");
        }

        lock (_lifecycleLock)
        {
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
        }
    }

    internal async Task ReceiveExtensionPayloadAsync(Stream destination, CancellationToken cancellationToken)
    {
        using CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _payloadShutdown.Token);
        ExtensionPayloadReceiver receiver = new(destination, operation.Token);
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_lifecycleState is LifecycleState.Disposed, this);
            if (_extensionPayloadReceiver is not null)
            {
                throw new InvalidOperationException("An extension payload is already being received from this worker.");
            }

            _extensionPayloadReceiver = receiver;
        }

        try
        {
            PayloadLog.TransferRequested(_payloadLogger, Id, receiver.RequestId);
            await SendStreamingMessageAsync(new()
            {
                RequestId = receiver.RequestId,
                GetExtensionPayloadRequest = new(),
            });
            Task completed = await Task.WhenAny(receiver.Completion, Completion).WaitAsync(operation.Token);
            if (!ReferenceEquals(completed, receiver.Completion))
            {
                throw new IOException("The worker stream closed before its extension payload completed.");
            }

            await receiver.Completion;
            PayloadLog.TransferReceived(_payloadLogger, Id, receiver.RequestId, receiver.ReceivedBytes);
        }
        finally
        {
            await operation.CancelAsync();
            await receiver.StopAsync();
            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_extensionPayloadReceiver, receiver))
                {
                    _extensionPayloadReceiver = null;
                }
            }
        }
    }

    protected override async ValueTask ProcessInboundMessageAsync(StreamingMessage message)
    {
        if (message.ContentCase is not StreamingMessage.ContentOneofCase.ExtensionPayloadResponse)
        {
            await base.ProcessInboundMessageAsync(message);
            return;
        }

        ExtensionPayloadReceiver receiver;
        lock (_lifecycleLock)
        {
            receiver = _extensionPayloadReceiver;
        }

        if (receiver is null || !string.Equals(message.RequestId, receiver.RequestId, StringComparison.Ordinal))
        {
            PayloadLog.LateResponse(_payloadLogger, Id);
            return;
        }

        await receiver.ReceiveAsync(message.ExtensionPayloadResponse);
    }

    protected override void OnFunctionLoadError(Exception exception) => _functionLoadFailure.TrySetResult(exception);

    protected override void OnMetadataRequestError(Exception exception) => FailPendingMetadataRequest(exception);

    protected override void OnChannelFailure(Exception exception) => FailPendingMetadataRequest(exception);

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

    protected override void Dispose(bool disposing)
    {
        lock (_lifecycleLock)
        {
            _lifecycleState = LifecycleState.Disposed;
        }

        Exception failure = null;
        if (disposing)
        {
            ObjectDisposedException exception = new(GetType().FullName);
            // Metadata callers did not request cancellation; fail so host startup can retry.
            FailPendingMetadataRequest(exception);
            StopAcceptingInvocations();
            Shutdown(exception);
            try
            {
                _payloadShutdown.Cancel();
            }
            catch (Exception cancellationException)
            {
                failure = cancellationException;
            }
        }

        try
        {
            base.Dispose(disposing);
        }
        catch (Exception exception)
        {
            failure = AggregateException.Combine(failure, exception);
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
            failure = AggregateException.Combine(failure, exception);
        }
        finally
        {
            if (disposing)
            {
                _payloadShutdown.Dispose();
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
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

    internal static Task GetHttpResponseCompletionTask(ScriptInvocationContext context)
    {
        HttpRequest request = context.Inputs?.Select(input => input.Val).OfType<HttpRequest>()
            .FirstOrDefault(request => request.HttpContext.Items.ContainsKey(ScriptConstants.AzureFunctionsHttpTriggerContext));
        if (request is null)
        {
            return Task.CompletedTask;
        }

        TaskCompletionSource responseCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        request.HttpContext.Response.OnCompleted(() =>
        {
            responseCompleted.TrySetResult();
            return Task.CompletedTask;
        });
        return responseCompleted.Task;
    }

    internal async Task ObserveRequestCompletionAsync(ScriptInvocationContext context, Task responseCompleted)
    {
        try
        {
            try
            {
                await context.ResultSource.Task.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The invoker owns failure reporting; this observer only tracks request lifetime.
            }

            if (context.Properties.TryGetValue(ScriptConstants.HttpProxyTask, out object value) && value is Task forwarding)
            {
                try
                {
                    await forwarding.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The proxy/invoker owns failure reporting; still wait for response completion.
                }
            }

            await responseCompleted.ConfigureAwait(false);
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _ongoingRequests--;
                RecordIdleTime();
            }
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

    private static partial class PayloadLog
    {
        [LoggerMessage(1, LogLevel.Debug, "Ignoring an extension payload response without a matching active request for worker {WorkerId}.")]
        public static partial void LateResponse(ILogger logger, string workerId);

        [LoggerMessage(2, LogLevel.Information, "Requesting extension payload from worker {WorkerId}, request {RequestId}.")]
        public static partial void TransferRequested(ILogger logger, string workerId, string requestId);

        [LoggerMessage(3, LogLevel.Information, "Received complete extension payload from worker {WorkerId}, request {RequestId}, {SizeBytes} bytes.")]
        public static partial void TransferReceived(ILogger logger, string workerId, string requestId, ulong sizeBytes);
    }
}
