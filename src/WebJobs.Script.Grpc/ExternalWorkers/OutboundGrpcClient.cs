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
/// Outbound client for the legacy <c>FunctionRpc.EventStream</c> relay.
/// </summary>
internal sealed partial class OutboundGrpcClient : OutboundGrpcClientBase
{
    private readonly IExtensionRpcEndpointRouter _extensionRpcEndpointRouter;

    /// <summary>
    /// Initializes a client that relays language-worker and extension traffic over one channel.
    /// </summary>
    /// <param name="eventManager">The event manager containing worker message channels.</param>
    /// <param name="logger">The logger used for client diagnostics.</param>
    /// <param name="extensionRpcEndpointRouter">The router for registered extension endpoints.</param>
    public OutboundGrpcClient(
        IScriptEventManager eventManager,
        ILogger<OutboundGrpcClient> logger,
        IExtensionRpcEndpointRouter extensionRpcEndpointRouter)
        : this(eventManager, logger, extensionRpcEndpointRouter, CreateGrpcChannel)
    {
    }

    /// <summary>
    /// Initializes a client with extension endpoint routing disabled.
    /// </summary>
    /// <param name="eventManager">The event manager containing worker message channels.</param>
    /// <param name="logger">The logger used for client diagnostics.</param>
    internal OutboundGrpcClient(IScriptEventManager eventManager, ILogger<OutboundGrpcClient> logger)
        : this(eventManager, logger, new UnavailableExtensionRpcEndpointRouter(), CreateGrpcChannel)
    {
    }

    /// <summary>
    /// Initializes a client with extension routing disabled and a custom channel factory.
    /// </summary>
    /// <param name="eventManager">The event manager containing worker message channels.</param>
    /// <param name="logger">The logger used for client diagnostics.</param>
    /// <param name="channelFactory">The factory used to create the outbound channel.</param>
    internal OutboundGrpcClient(
        IScriptEventManager eventManager,
        ILogger<OutboundGrpcClient> logger,
        Func<Uri, GrpcChannel> channelFactory)
        : this(eventManager, logger, new UnavailableExtensionRpcEndpointRouter(), channelFactory)
    {
    }

    /// <summary>
    /// Initializes a client with custom extension routing and channel creation.
    /// </summary>
    /// <param name="eventManager">The event manager containing worker message channels.</param>
    /// <param name="logger">The logger used for client diagnostics.</param>
    /// <param name="extensionRpcEndpointRouter">The router for registered extension endpoints.</param>
    /// <param name="channelFactory">The factory used to create the outbound channel.</param>
    internal OutboundGrpcClient(
        IScriptEventManager eventManager,
        ILogger<OutboundGrpcClient> logger,
        IExtensionRpcEndpointRouter extensionRpcEndpointRouter,
        Func<Uri, GrpcChannel> channelFactory)
        : base(eventManager, logger, channelFactory)
    {
        _extensionRpcEndpointRouter = extensionRpcEndpointRouter
            ?? throw new ArgumentNullException(nameof(extensionRpcEndpointRouter));
    }

    protected override OutboundGrpcStreamConnection OpenStream(
        string workerId,
        GrpcChannel channel,
        Channel<InboundGrpcEvent> inbound,
        Channel<OutboundGrpcEvent> outbound,
        CancellationToken cancellationToken)
    {
        var client = new FunctionRpc.FunctionRpcClient(channel);
        var call = client.EventStream(cancellationToken: cancellationToken);
        var extensionClient = new ExtensionRpc.ExtensionRpcClient(channel);

        return new OutboundGrpcStreamConnection(
            call,
            RunStreamAsync(
                workerId,
                call.RequestStream,
                call.ResponseStream,
                inbound,
                outbound.Reader,
                extensionClient,
                cancellationToken));
    }

    private async Task RunStreamAsync(
        string workerId,
        IClientStreamWriter<StreamingMessage> requestStream,
        IAsyncStreamReader<StreamingMessage> responseStream,
        Channel<InboundGrpcEvent> inbound,
        ChannelReader<OutboundGrpcEvent> outbound,
        ExtensionRpc.ExtensionRpcClient extensionClient,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource cancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var extensionStreamCoordinator = new ExtensionRpcStreamCoordinator(
            workerId,
            extensionClient,
            _extensionRpcEndpointRouter,
            Logger,
            cancellationTokenSource.Token);
        Task workerOutboundTask = PushOutbound(workerId, requestStream, outbound, cancellationTokenSource.Token);
        Task readerTask = PullInbound(workerId, responseStream, inbound, cancellationTokenSource.Token);
        Task extensionStreamTask = extensionStreamCoordinator.RunAsync();

        try
        {
            Task completedTask = await Task.WhenAny(workerOutboundTask, readerTask);
            await completedTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            cancellationTokenSource.Cancel();
            await Task.WhenAll(
                IgnoreCancellationAsync(workerOutboundTask, cancellationTokenSource.Token),
                IgnoreCancellationAsync(readerTask, cancellationTokenSource.Token),
                IgnoreCancellationAsync(extensionStreamTask, cancellationTokenSource.Token));
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
                    Log.WritingInvocationRequest(
                        Logger,
                        evt.Message.InvocationRequest.InvocationId,
                        workerId);
                }

                try
                {
                    await requestStream.WriteAsync(evt.Message, cancellationToken);
                }
                catch (Exception writeEx)
                {
                    Log.MessageWriteFailed(Logger, writeEx, evt.MessageType, workerId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            Log.PushOutboundFailed(Logger, ex, workerId);
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
                StreamingMessage message = responseStream.Current;
                if (message.ContentCase == MsgType.InvocationResponse
                    && !string.IsNullOrEmpty(message.InvocationResponse?.InvocationId))
                {
                    Log.InvocationResponseReceived(
                        Logger,
                        message.InvocationResponse.InvocationId,
                        workerId);
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
            if (ex is global::Grpc.Core.RpcException rpcEx)
            {
                Log.PullInboundGrpcFailed(
                    Logger,
                    ex,
                    workerId,
                    rpcEx.StatusCode,
                    rpcEx.Status.Detail);
            }
            else
            {
                Log.PullInboundFailed(Logger, ex, workerId, ex.GetType().FullName);
            }
        }
    }

    private static async Task IgnoreCancellationAsync(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            LogLevel.Trace,
            "Writing invocation request invocationId: {invocationId} to workerId: {workerId}")]
        public static partial void WritingInvocationRequest(
            ILogger logger,
            string invocationId,
            string workerId);

        [LoggerMessage(LogLevel.Error, "Error writing message type {messageType} to workerId: {workerId}")]
        public static partial void MessageWriteFailed(
            ILogger logger,
            Exception exception,
            MsgType messageType,
            string workerId);

        [LoggerMessage(LogLevel.Error, "Error pushing outbound messages to gRPC for workerId: {workerId}")]
        public static partial void PushOutboundFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(
            LogLevel.Trace,
            "Received invocation response for invocationId: {invocationId} from workerId: {workerId}")]
        public static partial void InvocationResponseReceived(
            ILogger logger,
            string invocationId,
            string workerId);

        [LoggerMessage(
            LogLevel.Error,
            "Error pulling inbound messages from gRPC for workerId: {workerId}. "
            + "GrpcStatusCode: {grpcStatusCode}, GrpcStatusDetail: {grpcStatusDetail}.")]
        public static partial void PullInboundGrpcFailed(
            ILogger logger,
            Exception exception,
            string workerId,
            StatusCode grpcStatusCode,
            string grpcStatusDetail);

        [LoggerMessage(
            LogLevel.Error,
            "Error pulling inbound messages from gRPC for workerId: {workerId}. ExceptionType: {exceptionType}.")]
        public static partial void PullInboundFailed(
            ILogger logger,
            Exception exception,
            string workerId,
            string? exceptionType);
    }
}
