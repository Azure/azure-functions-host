// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
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
    private readonly IScriptEventManager _eventManager;
    private readonly ILogger _logger;

    private GrpcChannel? _channel;
    private AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage>? _call;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboundGrpcClient"/> class.
    /// </summary>
    /// <param name="eventManager">The event manager that holds per-worker gRPC channels.</param>
    /// <param name="logger">Logger instance.</param>
    public OutboundGrpcClient(IScriptEventManager eventManager, ILogger<OutboundGrpcClient> logger)
    {
        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Connects to the remote gRPC endpoint and starts the bidirectional message pump.
    /// </summary>
    /// <param name="workerId">The worker identifier whose channels have been pre-registered via
    /// <see cref="GrpcEventExtensions.AddGrpcChannels"/>.</param>
    /// <param name="endpoint">The URI of the remote <c>FunctionRpc</c> service.</param>
    /// <param name="cancellationToken">Token to cancel the connection attempt.</param>
    /// <returns>A task that completes once the stream is established and background pumps are running.</returns>
    public Task ConnectAsync(string workerId, Uri endpoint, CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _channel = GrpcChannel.ForAddress(endpoint);
            var client = new FunctionRpc.FunctionRpcClient(_channel);
            _call = client.EventStream(cancellationToken: _cts.Token);

            if (!_eventManager.TryGetGrpcChannels(workerId, out var inbound, out var outbound))
            {
                throw new InvalidOperationException($"No pre-registered gRPC channels found for worker '{workerId}'.");
            }

            _ = PushOutbound(workerId, _call.RequestStream, outbound.Reader, _cts.Token);
            _ = PullInbound(workerId, _call.ResponseStream, inbound, _cts.Token);

            _logger.LogDebug("Outbound gRPC client connected to {endpoint} for workerId: {workerId}", endpoint, workerId);

            return Task.CompletedTask;
        }
        catch
        {
            _call?.Dispose();

            if (_channel is not null)
            {
                _channel.Dispose();
            }

            _cts?.Dispose();
            throw;
        }
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
            _logger.LogError(ex, "Error pulling inbound messages from gRPC for workerId: {workerId}", workerId);
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
}
