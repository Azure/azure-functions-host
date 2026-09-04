using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Implements the runtime-facing extension RPC service and attaches its stream to the coordinator.
/// </summary>
/// <param name="options">The configured runtime gRPC endpoint.</param>
/// <param name="streamCoordinator">The coordinator that owns the active extension RPC stream.</param>
internal sealed class ExtensionRpcRelay(RelayOptions options, ExtensionRpcStreamCoordinator streamCoordinator)
    : ExtensionRpc.ExtensionRpcBase
{
    /// <inheritdoc/>
    public override Task EventStream(
        IAsyncStreamReader<ExtensionRpcMessage> requestStream,
        IServerStreamWriter<ExtensionRpcMessage> responseStream,
        ServerCallContext context)
    {
        if (context.GetHttpContext().Connection.LocalPort != options.RuntimeGrpcPort)
        {
            throw new Grpc.Core.RpcException(new Status(
                StatusCode.PermissionDenied,
                "ExtensionRpc is only available on the runtime gRPC port."));
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
        await using ExtensionRpcStreamLease lease = streamCoordinator.Open(cancellationTokenSource.Token);
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
