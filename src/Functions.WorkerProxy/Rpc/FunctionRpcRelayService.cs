// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Azure.Functions.WorkerProxy;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using GrpcRpcException = Grpc.Core.RpcException;

namespace Azure.Functions.WorkerProxy.Rpc;

/// <summary>
/// Adapts FunctionRpc endpoint requests to the shared relay.
/// </summary>
/// <remarks>
/// The local listener port determines stream ownership, so callers cannot select or change
/// ownership through headers or FunctionRpc messages.
/// </remarks>
internal sealed class FunctionRpcRelayService(FunctionRpcRelay relay, WorkerProxyEndpointConfiguration endpoints) : FunctionRpc.FunctionRpcBase
{
    /// <inheritdoc />
    public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream,
        IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
    {
        HttpContext httpContext = context.GetHttpContext();
        if (!endpoints.TryGetRelaySide(httpContext.Connection.LocalPort, out FunctionRpcRelaySide side))
        {
            throw new GrpcRpcException(new Status(StatusCode.Unimplemented, "FunctionRpc is unavailable on this listener."));
        }

        FunctionRpcRelayTerminalState terminalState;
        try
        {
            terminalState = await relay.AttachAsync(side, requestStream, responseStream, context.CancellationToken);
        }
        catch (FunctionRpcRelayAttachmentException exception)
        {
            StatusCode statusCode = exception.Failure switch
            {
                FunctionRpcRelayAttachmentFailure.Duplicate => StatusCode.AlreadyExists,
                FunctionRpcRelayAttachmentFailure.PreviousSessionTearingDown => StatusCode.Unavailable,
                FunctionRpcRelayAttachmentFailure.Shutdown => StatusCode.Unavailable,
                _ => StatusCode.Unknown
            };

            throw new GrpcRpcException(new Status(statusCode, exception.Message));
        }

        context.Status = terminalState.Reason switch
        {
            FunctionRpcRelayTerminationReason.PeerClosed =>
                new Status(StatusCode.Unavailable, "A FunctionRpc relay peer closed its stream."),
            FunctionRpcRelayTerminationReason.Canceled =>
                new Status(StatusCode.Cancelled, "The FunctionRpc relay session was canceled."),
            FunctionRpcRelayTerminationReason.Faulted =>
                new Status(StatusCode.Unavailable, "A FunctionRpc relay stream operation failed."),
            FunctionRpcRelayTerminationReason.Shutdown =>
                new Status(StatusCode.Unavailable, "The FunctionRpc relay is shutting down."),
            _ => new Status(StatusCode.Unknown, "The FunctionRpc relay session terminated.")
        };

        if (terminalState.Reason == FunctionRpcRelayTerminationReason.Shutdown)
        {
            httpContext.Abort();
        }

        return;
    }
}
