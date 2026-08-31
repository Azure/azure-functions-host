// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using GrpcRpcException = Grpc.Core.RpcException;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Adapts one side-specific FunctionRpc endpoint to the shared relay.
/// </summary>
/// <remarks>
/// The endpoint application supplies the side identity through dependency injection, so callers
/// cannot select or change stream ownership through headers or FunctionRpc messages.
/// </remarks>
internal sealed class FunctionRpcRelayService : FunctionRpc.FunctionRpcBase
{
    private readonly FunctionRpcRelay _relay;
    private readonly FunctionRpcRelayEndpoint _endpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionRpcRelayService"/> class.
    /// </summary>
    /// <param name="relay">The shared FunctionRpc relay.</param>
    /// <param name="endpoint">The immutable side owned by this endpoint application.</param>
    public FunctionRpcRelayService(FunctionRpcRelay relay, FunctionRpcRelayEndpoint endpoint)
    {
        _relay = relay;
        _endpoint = endpoint;
    }

    /// <inheritdoc />
    public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream,
        IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
    {
        try
        {
            await _relay.AttachAsync(_endpoint.Side, requestStream, responseStream, context.CancellationToken);
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
        catch (FunctionRpcRelayTerminatedException exception)
        {
            if (exception.TerminalState.Reason == FunctionRpcRelayTerminationReason.Shutdown)
            {
                context.GetHttpContext().Abort();
            }

            StatusCode statusCode = exception.TerminalState.Reason switch
            {
                FunctionRpcRelayTerminationReason.PeerClosed => StatusCode.Unavailable,
                FunctionRpcRelayTerminationReason.Canceled => StatusCode.Cancelled,
                FunctionRpcRelayTerminationReason.Faulted => StatusCode.Unavailable,
                FunctionRpcRelayTerminationReason.Shutdown => StatusCode.Unavailable,
                _ => StatusCode.Unknown
            };
            string detail = exception.TerminalState.Reason switch
            {
                FunctionRpcRelayTerminationReason.PeerClosed => "A FunctionRpc relay peer closed its stream.",
                FunctionRpcRelayTerminationReason.Canceled => "The FunctionRpc relay session was canceled.",
                FunctionRpcRelayTerminationReason.Faulted => "A FunctionRpc relay stream operation failed.",
                FunctionRpcRelayTerminationReason.Shutdown => "The FunctionRpc relay is shutting down.",
                _ => "The FunctionRpc relay session terminated."
            };

            throw new GrpcRpcException(new Status(statusCode, detail));
        }
    }
}
