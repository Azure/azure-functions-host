// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using GrpcRpcException = Grpc.Core.RpcException;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Implements the runtime-facing extension RPC service and attaches its stream to the coordinator.
/// </summary>
/// <param name="endpoints">The WorkerProxy listener configuration.</param>
/// <param name="streamCoordinator">The coordinator that owns the active extension RPC stream.</param>
internal sealed class ExtensionRpcRelay(
    WorkerProxyEndpointConfiguration endpoints,
    ExtensionRpcStreamCoordinator streamCoordinator)
    : ExtensionRpc.ExtensionRpcBase
{
    /// <inheritdoc />
    public override Task EventStream(
        IAsyncStreamReader<ExtensionRpcMessage> requestStream,
        IServerStreamWriter<ExtensionRpcMessage> responseStream,
        ServerCallContext context)
    {
        HttpContext httpContext = context.GetHttpContext();
        if (!endpoints.TryGetRelaySide(httpContext.Connection.LocalPort, out FunctionRpcRelaySide side)
            || side is not FunctionRpcRelaySide.Runtime)
        {
            throw new GrpcRpcException(
                new Status(StatusCode.PermissionDenied, "ExtensionRpc is only available on the runtime gRPC port."));
        }

        return RelayAsync(requestStream, responseStream, context.CancellationToken);
    }

    /// <summary>
    /// Relays inbound and outbound lifecycle messages for one physical extension RPC stream.
    /// </summary>
    /// <param name="requestStream">Messages received from the host runtime.</param>
    /// <param name="responseStream">Messages sent to the host runtime.</param>
    /// <param name="cancellationToken">A token that is cancelled when the stream ends.</param>
    /// <returns>A task that represents the stream lifetime.</returns>
    internal async Task RelayAsync(
        IAsyncStreamReader<ExtensionRpcMessage> requestStream,
        IServerStreamWriter<ExtensionRpcMessage> responseStream,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource cancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using ExtensionRpcStreamLease lease = OpenStream(cancellationTokenSource.Token);
        Task readTask = ReadInboundAsync(lease.Stream, requestStream, cancellationTokenSource.Token);
        Task writeTask = WriteOutboundAsync(lease.Stream, responseStream, cancellationTokenSource.Token);

        try
        {
            Task completedTask = await Task.WhenAny(readTask, writeTask);
            await completedTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            cancellationTokenSource.Cancel();
            await Task.WhenAll(
                IgnoreCancellationAsync(readTask, cancellationTokenSource.Token),
                IgnoreCancellationAsync(writeTask, cancellationTokenSource.Token));
        }
    }

    private ExtensionRpcStreamLease OpenStream(CancellationToken cancellationToken)
    {
        try
        {
            return streamCoordinator.Open(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new GrpcRpcException(new Status(StatusCode.AlreadyExists, exception.Message));
        }
    }

    private static async Task ReadInboundAsync(
        ExtensionRpcStream stream,
        IAsyncStreamReader<ExtensionRpcMessage> requestStream,
        CancellationToken cancellationToken)
    {
        while (await requestStream.MoveNext(cancellationToken))
        {
            await stream.HandleInboundAsync(requestStream.Current, cancellationToken);
        }
    }

    private static async Task WriteOutboundAsync(
        ExtensionRpcStream stream,
        IServerStreamWriter<ExtensionRpcMessage> responseStream,
        CancellationToken cancellationToken)
    {
        await foreach (ExtensionRpcMessage message in stream.Outbound.ReadAllAsync(cancellationToken))
        {
            await responseStream.WriteAsync(message, cancellationToken);
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
}
