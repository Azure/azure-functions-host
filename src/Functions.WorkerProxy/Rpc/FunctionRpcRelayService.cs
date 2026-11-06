// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Azure.Functions.WorkerProxy;
using Azure.Functions.WorkerProxy.Authentication;
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
internal sealed class FunctionRpcRelayService(
    FunctionRpcRelay relay,
    WorkerProxyEndpointConfiguration endpoints,
    IRuntimeAuthenticationKeyProvider keyProvider,
    TimeProvider timeProvider) : FunctionRpc.FunctionRpcBase
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
        PodIdentity? runtimeIdentity = side == FunctionRpcRelaySide.Runtime
            ? ReadRuntimeIdentity(context)
            : null;
        try
        {
            IServerStreamWriter<StreamingMessage> writer = side is FunctionRpcRelaySide.Runtime
                ? new RuntimeResponseWriter(responseStream, keyProvider, timeProvider)
                : responseStream;
            terminalState = await relay.AttachAsync(side, requestStream, writer, context.CancellationToken, runtimeIdentity);
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

    private static PodIdentity? ReadRuntimeIdentity(Metadata headers)
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
            PodIdentity? identity = JsonSerializer.Deserialize(
                values[0].ValueBytes,
                WorkerProxyJsonContext.Default.PodIdentity);
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

    private static PodIdentity? ReadRuntimeIdentity(ServerCallContext context)
    {
        try
        {
            return ReadRuntimeIdentity(context.RequestHeaders);
        }
        catch (FormatException)
        {
            throw InvalidRuntimeIdentity();
        }
    }

    private static GrpcRpcException InvalidRuntimeIdentity()
    {
        return new(new Status(StatusCode.InvalidArgument, "Invalid Runtime identity metadata."));
    }

    private sealed class RuntimeResponseWriter(
        IServerStreamWriter<StreamingMessage> inner,
        IRuntimeAuthenticationKeyProvider keyProvider,
        TimeProvider timeProvider) : IServerStreamWriter<StreamingMessage>
    {
        public WriteOptions? WriteOptions
        {
            get => inner.WriteOptions;
            set => inner.WriteOptions = value;
        }

        public Task WriteAsync(StreamingMessage message) => inner.WriteAsync(Prepare(message));

        public Task WriteAsync(StreamingMessage message, CancellationToken cancellationToken)
            => inner.WriteAsync(Prepare(message), cancellationToken);

        private StreamingMessage Prepare(StreamingMessage message)
        {
            if (message.WorkerInitResponse is null)
            {
                return message;
            }

            if (message.WorkerInitResponse.Result is not
                { Status: StatusResult.Types.Status.Success or StatusResult.Types.Status.Failure or StatusResult.Types.Status.Cancelled })
            {
                throw new InvalidDataException("The worker initialization response must have a known result status.");
            }

            // Keep the authentication proof outside the relay's ordinary capability snapshots.
            StreamingMessage transport = message.Clone();
            RuntimeWorkerAuthentication.RemoveAppProof(transport.WorkerInitResponse.Capabilities);
            RuntimeAppAuthenticationKeys keys = keyProvider.GetAppKeys()
                ?? throw new InvalidOperationException("WorkerProxy assignment is unavailable for application authentication.");
            RuntimeWorkerAuthentication.SetAppProof(
                transport.WorkerInitResponse.Capabilities,
                RuntimeWorkerAuthentication.CreateAppProof(keys.AppKey.Span, keys.AppName, keys.WorkerId, timeProvider.GetUtcNow()));

            return transport;
        }
    }
}
