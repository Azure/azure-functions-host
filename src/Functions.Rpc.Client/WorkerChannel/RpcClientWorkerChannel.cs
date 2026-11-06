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
    private readonly Lock _invocationAdmissionLock = new();
    private readonly Lock _lifecycleLock = new();
    private readonly ILogger _payloadLogger;
    private readonly CancellationTokenSource _payloadShutdown = new();
    private readonly TaskCompletionSource _startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly RuntimeWorkerCredentials _credentials;
    private TimeProvider _activityTimeProvider = TimeProvider.System;
    private long _idleSince;
    private bool _hasReceivedRequest;
    private Task _drainTask;
    private bool _draining;
    private bool _httpCapacityReserved;
    private bool? _isAlwaysReady;
    private Uri _httpEndpoint;
    private ExtensionPayloadReceiver _extensionPayloadReceiver;
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
        IHttpProxyService httpProxyService,
        RuntimeWorkerCredentials credentials)
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
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        Completion = ownedChannel.Reader.Completion;
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

    /// <summary>
    /// Gets a value indicating whether the captured startup assignment belongs to the HTTP function group.
    /// </summary>
    internal bool IsHttpFunctionGroup => string.Equals(FunctionGroupName, FunctionGroups.Http, StringComparison.OrdinalIgnoreCase);

    internal Task<Exception> FunctionLoadFailure => _functionLoadFailure.Task;

    internal bool IsTerminated => Completion.IsCompleted || FunctionLoadFailure.IsCompleted;

    internal int OngoingRequests => AcceptedInvocationCount;

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

    internal void StartIdleTracking(TimeProvider timeProvider)
    {
        lock (_invocationAdmissionLock)
        {
            _activityTimeProvider = timeProvider;
            _idleSince = timeProvider.GetTimestamp();
        }
    }

    internal bool TryClaimIdleRetirement(TimeSpan threshold, out TimeSpan idleDuration)
    {
        lock (_invocationAdmissionLock)
        {
            idleDuration = _activityTimeProvider.GetElapsedTime(_idleSince);
            if (!IsHttpFunctionGroup || _isAlwaysReady is true || !IsUsableForInvocationsLocked()
                || AcceptedInvocationCount != 0
                || idleDuration <= (_hasReceivedRequest ? threshold : InitialIdleThreshold))
            {
                return false;
            }

            _draining = true;
            return true;
        }
    }

    internal void StopAcceptingInvocations()
    {
        lock (_invocationAdmissionLock)
        {
            _draining = true;
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
            else
            {
                _hasReceivedRequest = true;
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

            return BeginDrainLocked();
        }
    }

    internal Task TryBeginRuntimeDrain()
    {
        lock (_invocationAdmissionLock)
        {
            if (_draining)
            {
                return _drainTask;
            }

            if (IsTerminated)
            {
                return null;
            }

            return BeginDrainLocked();
        }
    }

    internal void ExpireRuntimeDrain(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_invocationAdmissionLock)
        {
            _draining = true;
        }

        Shutdown(exception);
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

        lock (_lifecycleLock)
        {
            if (FunctionGroupName is not null)
            {
                return;
            }

            if (!response.Capabilities.TryGetValue(FunctionGroupNameCapability, out string functionGroupName)
                || string.IsNullOrWhiteSpace(functionGroupName))
            {
                throw new InvalidOperationException("A client-backed worker must advertise its assigned FunctionGroupName.");
            }

            int? maxHttpConcurrency = null;
            if (string.Equals(functionGroupName, FunctionGroups.Http, StringComparison.OrdinalIgnoreCase))
            {
                response.Capabilities.TryGetValue(MaxHttpConcurrencyCapability, out string suppliedConcurrency);
                if (!int.TryParse(suppliedConcurrency, NumberStyles.None, CultureInfo.InvariantCulture, out int concurrency)
                    || concurrency is < 1 or > 1000)
                {
                    throw new InvalidOperationException("An HTTP worker must advertise MaxHttpConcurrency as an integer between 1 and 1000.");
                }

                maxHttpConcurrency = concurrency;
            }

            // Read only the original response: capability updates must not reassign an initialized channel.
            HostConfiguration = response.HostConfiguration?.Clone();
            FunctionGroupName = functionGroupName;
            MaxHttpConcurrency = maxHttpConcurrency;
            response.Capabilities.TryGetValue(IsAlwaysReadyCapability, out string isAlwaysReady);
            _isAlwaysReady = bool.TryParse(isAlwaysReady, out bool parsedIsAlwaysReady)
                ? parsedIsAlwaysReady
                : null;
            if (IsHttpFunctionGroup && _isAlwaysReady is null)
            {
                _payloadLogger.LogWarning("Worker {WorkerId} did not advertise valid IsAlwaysReady metadata; treating this worker as on-demand for idle retirement.", Id);
            }
        }
    }

    internal override void WorkerInitResponse(StreamingMessage initMessage)
    {
        string proof = RuntimeWorkerAuthentication.RemoveAppProof(initMessage.WorkerInitResponse.Capabilities);
        _credentials.ValidateAppProof(proof);
        if (initMessage.WorkerInitResponse.Result is not
            { Status: StatusResult.Types.Status.Success or StatusResult.Types.Status.Failure or StatusResult.Types.Status.Cancelled })
        {
            throw new WorkerLinkException(
                WorkerLinkFailureReason.Unavailable,
                "The worker initialization response must have a known result status.");
        }

        base.WorkerInitResponse(initMessage);
    }

    internal override void UpdateCapabilities(
        IDictionary<string, string> fields,
        GrpcCapabilitiesUpdateStrategy strategy)
    {
        fields = new Dictionary<string, string>(fields, StringComparer.Ordinal);
        RuntimeWorkerAuthentication.RemoveAppProof(fields);
        if (fields.TryGetValue(RpcWorkerConstants.HttpUri, out string advertisedHttpUri)
            && !string.IsNullOrEmpty(advertisedHttpUri))
        {
            if (!Uri.TryCreate(advertisedHttpUri, UriKind.Absolute, out Uri endpoint))
            {
                throw new WorkerLinkException(
                    WorkerLinkFailureReason.Unavailable,
                    "The advertised WorkerProxy HTTP origin is invalid.");
            }

            try
            {
                RpcClientFactory.ValidateEndpoint(endpoint);
            }
            catch (ArgumentException exception)
            {
                throw new WorkerLinkException(
                    WorkerLinkFailureReason.Unavailable,
                    "The advertised WorkerProxy HTTP origin is invalid.",
                    exception);
            }

            if (_httpEndpoint is not null
                && !string.Equals(_httpEndpoint.AbsoluteUri, endpoint.AbsoluteUri, StringComparison.Ordinal))
            {
                throw new WorkerLinkException(
                    WorkerLinkFailureReason.Unavailable,
                    "The initialized WorkerProxy HTTP origin cannot change.");
            }

            _httpEndpoint = endpoint;
        }

        base.UpdateCapabilities(fields, strategy);
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

    protected override Task GetInvocationCompletionTask(ScriptInvocationContext context)
    {
        if (!IsHttpFunctionGroup)
        {
            return base.GetInvocationCompletionTask(context);
        }

        HttpRequest request = context.Inputs?.Select(input => input.Val).OfType<HttpRequest>()
            .FirstOrDefault(request => request.HttpContext.Items.ContainsKey(ScriptConstants.AzureFunctionsHttpTriggerContext));
        Task responseCompleted = Task.CompletedTask;
        if (request is not null)
        {
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            request.HttpContext.Response.OnCompleted(() =>
            {
                completion.TrySetResult();
                return Task.CompletedTask;
            });
            responseCompleted = completion.Task;
        }

        return ObserveInvocationCompletionAsync(context, responseCompleted);
    }

    private static async Task ObserveInvocationCompletionAsync(ScriptInvocationContext context, Task responseCompleted)
    {
        // The invoker/proxy owns error reporting. Cancellation is not proof that the response body has finished.
        await ((Task)context.ResultSource.Task).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (context.Properties.TryGetValue(ScriptConstants.HttpProxyTask, out object value) && value is Task forwarding)
        {
            await forwarding.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        await responseCompleted.ConfigureAwait(false);
    }

    protected override void CompleteAcceptedInvocation(ScriptInvocationContext context)
    {
        lock (_invocationAdmissionLock)
        {
            base.CompleteAcceptedInvocation(context);
            if (AcceptedInvocationCount == 0)
            {
                _idleSince = _activityTimeProvider.GetTimestamp();
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

        Exception failure = null;
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

    private Task BeginDrainLocked()
    {
        _draining = true;
        // Drain needs every accepted result to become terminal, not necessarily successful.
        _drainTask = DrainInvocationsAsync()
            .ContinueWith(
                static completed =>
                {
                    _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        return _drainTask;
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
