// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.State;
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
    private const string RuntimeIdentityHeaderName = "x-ms-functions-runtime-identity-bin";

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
        SplitPodIdentity? runtimeIdentity = side == FunctionRpcRelaySide.Runtime
            ? ReadRuntimeIdentity(context.RequestHeaders)
            : null;
        try
        {
            terminalState = await relay.AttachAsync(side, requestStream, responseStream, context.CancellationToken, runtimeIdentity);
        }
        catch (FunctionRpcRelayAttachmentException exception)
        {
            StatusCode statusCode = exception.Failure switch
            {
                FunctionRpcRelayAttachmentFailure.Duplicate => StatusCode.AlreadyExists,
                FunctionRpcRelayAttachmentFailure.PodStateRejected => StatusCode.Unavailable,
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

    private static SplitPodIdentity? ReadRuntimeIdentity(Metadata headers)
    {
        Metadata.Entry[] values = headers
            .Where(static header => string.Equals(header.Key, RuntimeIdentityHeaderName, StringComparison.Ordinal))
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }

        if (values.Length != 1 || !values[0].IsBinary)
        {
            throw InvalidRuntimeIdentity();
        }

        try
        {
            SplitPodIdentity? identity = JsonSerializer.Deserialize(
                values[0].ValueBytes,
                WorkerProxyJsonContext.Default.SplitPodIdentity);
            if (identity is not null && !string.IsNullOrWhiteSpace(identity.ResourceNamespace) &&
                !string.IsNullOrWhiteSpace(identity.PodName) && identity.App is not null &&
                !string.IsNullOrWhiteSpace(identity.App.FunctionAppName) && identity.App.FunctionAppSiteId > 0 &&
                !string.IsNullOrWhiteSpace(identity.App.AntaresStampName))
            {
                return identity;
            }
        }
        catch (JsonException)
        {
            throw InvalidRuntimeIdentity();
        }

        throw InvalidRuntimeIdentity();
    }

    private static GrpcRpcException InvalidRuntimeIdentity()
    {
        return new(new Status(StatusCode.InvalidArgument, "Invalid Runtime identity metadata."));
    }
}
