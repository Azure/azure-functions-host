// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;
using MsgType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.StreamingMessage.ContentOneofCase;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// gRPC client that connects outbound to a remote endpoint implementing
/// <c>FunctionRpc.EventStream</c>. Establishes a bidirectional stream and
/// bridges it to the in-process <see cref="Channel{T}"/> infrastructure consumed by
/// <see cref="ConnectedWorkerChannel"/>.
/// </summary>
internal class OutboundGrpcClient : IOutboundGrpcClient
{
    internal static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan DefaultReadyTimeout = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan DefaultKeepAlivePingDelay = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan DefaultKeepAlivePingTimeout = TimeSpan.FromSeconds(10);

    // The runtime-to-worker link is a single host-local pair with no load
    // concerns, so we replace gRPC's default ExponentialBackoffPolicy with
    // a constant 25 ms policy. Within the 1 s DefaultReadyTimeout that
    // yields ~40 retry attempts — maximising the chance of catching the
    // worker proxy at the moment its gRPC listener binds, while still
    // imposing zero meaningful load on either side (each failed attempt is
    // just a fast-fail ECONNREFUSED on Linux).
    //
    // The corresponding ExponentialBackoffPolicy with 25 ms initial would
    // stretch to 25, 40, 64, 102, 164, 262, 419, ... ms — making the
    // worst-case "discovery latency" several hundred ms once the listener
    // becomes available. Constant 25 ms bounds that worst case at 25 ms.
    internal static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromMilliseconds(25);

    private readonly IScriptEventManager _eventManager;
    private readonly ILogger _logger;
    private readonly Func<Uri, GrpcChannel> _channelFactory;

    private GrpcChannel? _channel;
    private AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage>? _call;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboundGrpcClient"/> class.
    /// </summary>
    /// <param name="eventManager">The event manager that holds per-worker gRPC channels.</param>
    /// <param name="logger">Logger instance.</param>
    public OutboundGrpcClient(IScriptEventManager eventManager, ILogger<OutboundGrpcClient> logger)
        : this(eventManager, logger, CreateGrpcChannel)
    {
    }

    internal OutboundGrpcClient(
        IScriptEventManager eventManager,
        ILogger<OutboundGrpcClient> logger,
        Func<Uri, GrpcChannel> channelFactory)
    {
        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
    }

    /// <inheritdoc/>
    public Task? InboundPumpTask { get; private set; }

    /// <summary>
    /// Connects to the remote gRPC endpoint and starts the bidirectional message pump.
    /// </summary>
    /// <param name="workerId">The worker identifier whose channels have been pre-registered via
    /// <see cref="GrpcEventExtensions.AddGrpcChannels"/>.</param>
    /// <param name="endpoint">The URI of the remote <c>FunctionRpc</c> service.</param>
    /// <param name="cancellationToken">Token to cancel the connection attempt.</param>
    /// <returns>A task that completes once the stream is established and background pumps are running.</returns>
    public async Task ConnectAsync(string workerId, Uri endpoint, CancellationToken cancellationToken)
    {
        var connectStart = Stopwatch.GetTimestamp();
        _logger.LogInformation("OutboundGrpcClient connect started. WorkerId: {workerId}, Endpoint: {endpoint}.", workerId, endpoint);

        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _channel = _channelFactory(endpoint);

            using var readinessCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            readinessCts.CancelAfter(DefaultReadyTimeout);

            var readinessStart = Stopwatch.GetTimestamp();
            await _channel.ConnectAsync(readinessCts.Token);
            _logger.LogInformation("OutboundGrpcClient channel connected. WorkerId: {workerId}, Endpoint: {endpoint}, StepElapsedMilliseconds: {stepElapsedMilliseconds}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, endpoint, Stopwatch.GetElapsedTime(readinessStart).TotalMilliseconds, Stopwatch.GetElapsedTime(connectStart).TotalMilliseconds);

            var client = new FunctionRpc.FunctionRpcClient(_channel);
            _call = client.EventStream(cancellationToken: _cts.Token);

            if (!_eventManager.TryGetGrpcChannels(workerId, out var inbound, out var outbound))
            {
                throw new InvalidOperationException($"No pre-registered gRPC channels found for worker '{workerId}'.");
            }

            _ = PushOutbound(workerId, _call.RequestStream, outbound.Reader, _cts.Token);
            InboundPumpTask = PullInbound(workerId, _call.ResponseStream, inbound, _cts.Token);

            _logger.LogInformation("OutboundGrpcClient stream established. WorkerId: {workerId}, Endpoint: {endpoint}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, endpoint, Stopwatch.GetElapsedTime(connectStart).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OutboundGrpcClient connect failed. WorkerId: {workerId}, Endpoint: {endpoint}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, endpoint, Stopwatch.GetElapsedTime(connectStart).TotalMilliseconds);

            // Dispose-and-null each resource so a subsequent DisposeAsync
            // call cannot operate on a disposed CancellationTokenSource
            // (cts.CancelAsync throws ObjectDisposedException, and the
            // for-loop retry in WorkerConnectionService would silently
            // escape with that exception instead of iterating).
            Interlocked.Exchange(ref _call, null)?.Dispose();
            Interlocked.Exchange(ref _channel, null)?.Dispose();
            Interlocked.Exchange(ref _cts, null)?.Dispose();
            throw;
        }
    }

    internal static GrpcChannel CreateGrpcChannel(Uri endpoint)
    {
        return GrpcChannel.ForAddress(endpoint, CreateGrpcChannelOptions());
    }

    internal static GrpcChannelOptions CreateGrpcChannelOptions()
    {
        return new GrpcChannelOptions
        {
            HttpHandler = CreateHttpHandler(),
            ServiceProvider = BackoffPolicyServiceProvider.Instance
        };
    }

    private static SocketsHttpHandler CreateHttpHandler()
    {
        return new SocketsHttpHandler
        {
            ConnectTimeout = DefaultConnectTimeout,
            KeepAlivePingDelay = DefaultKeepAlivePingDelay,
            KeepAlivePingTimeout = DefaultKeepAlivePingTimeout,
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always
        };
    }

    private async Task PushOutbound(
        string workerId,
        IClientStreamWriter<StreamingMessage> requestStream,
        ChannelReader<OutboundGrpcEvent> source,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Yield();
            await foreach (var evt in source.ReadAllAsync(cancellationToken))
            {
                if (evt.MessageType == MsgType.InvocationRequest)
                {
                    _logger.LogTrace("Writing invocation request invocationId: {invocationId} to workerId: {workerId}",
                        evt.Message.InvocationRequest.InvocationId, workerId);
                }

                try
                {
                    await requestStream.WriteAsync(evt.Message);
                }
                catch (Exception writeEx)
                {
                    _logger.LogError(writeEx, "Error writing message type {messageType} to workerId: {workerId}",
                        evt.MessageType, workerId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing outbound messages to gRPC for workerId: {workerId}", workerId);
        }
    }

    private async Task PullInbound(
        string workerId,
        IAsyncStreamReader<StreamingMessage> responseStream,
        Channel<InboundGrpcEvent> inbound,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Yield();
            while (await responseStream.MoveNext(cancellationToken))
            {
                var message = responseStream.Current;

                if (message.ContentCase == MsgType.InvocationResponse
                    && !string.IsNullOrEmpty(message.InvocationResponse?.InvocationId))
                {
                    _logger.LogTrace("Received invocation response for invocationId: {invocationId} from workerId: {workerId}",
                        message.InvocationResponse.InvocationId, workerId);
                }

                var inboundEvent = new InboundGrpcEvent(workerId, message);
                if (!inbound.Writer.TryWrite(inboundEvent))
                {
                    await inbound.Writer.WriteAsync(inboundEvent, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (global::Grpc.Core.RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            // Expected when the call is cancelled.
        }
        catch (Exception ex)
        {
            // Log full gRPC status detail to help diagnose whether the worker-proxy
            // container is not started, gRPC service not registered, or network issue.
            if (ex is global::Grpc.Core.RpcException rpcEx)
            {
                _logger.LogError(ex, "Error pulling inbound messages from gRPC for workerId: {workerId}. GrpcStatusCode: {grpcStatusCode}, GrpcStatusDetail: {grpcStatusDetail}.",
                    workerId, rpcEx.StatusCode, rpcEx.Status.Detail);
            }
            else
            {
                _logger.LogError(ex, "Error pulling inbound messages from gRPC for workerId: {workerId}. ExceptionType: {exceptionType}.",
                    workerId, ex.GetType().FullName);
            }
        }
    }

    /// <summary>
    /// Cancels background pumps and releases the gRPC channel and call resources.
    /// This method is idempotent and safe to call multiple times.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        Interlocked.Exchange(ref _call, null)?.Dispose();
        Interlocked.Exchange(ref _channel, null)?.Dispose();
    }

    /// <summary>
    /// Minimal <see cref="IServiceProvider"/> that supplies only the
    /// <see cref="IBackoffPolicyFactory"/> override used by
    /// <see cref="CreateGrpcChannelOptions"/>. Returning <c>null</c> for
    /// every other service makes gRPC fall through to its built-in defaults.
    /// Implemented as a singleton because the factory is stateless.
    /// </summary>
    private sealed class BackoffPolicyServiceProvider : IServiceProvider
    {
        public static readonly BackoffPolicyServiceProvider Instance = new BackoffPolicyServiceProvider();

        private static readonly IBackoffPolicyFactory _factory = new ConstantBackoffPolicyFactory(DefaultRetryInterval);

        private BackoffPolicyServiceProvider()
        {
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IBackoffPolicyFactory))
            {
                return _factory;
            }

            return null;
        }
    }
}
