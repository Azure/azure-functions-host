// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
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
    private readonly TaskCompletionSource<Exception> _functionLoadFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _lifecycleLock = new();
    private readonly ILogger _payloadLogger;
    private readonly CancellationTokenSource _payloadShutdown = new();
    private readonly TaskCompletionSource _startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        _payloadLogger = logger;
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
    internal bool IsReadyForInvocations => !IsTerminated && IsChannelReadyForInvocations();

    internal string ExtensionPayloadCapability => GetCapabilityState(WorkerExtensionPayloadManager.CapabilityName);

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

    protected override void Dispose(bool disposing)
    {
        lock (_lifecycleLock)
        {
            _lifecycleState = LifecycleState.Disposed;
        }

        Exception failure = null;
        if (disposing)
        {
            // Metadata callers did not request cancellation; fail so host startup can retry.
            FailPendingMetadataRequest(new ObjectDisposedException(GetType().FullName));
            try
            {
                _payloadShutdown.Cancel();
            }
            catch (Exception exception)
            {
                failure = exception;
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
