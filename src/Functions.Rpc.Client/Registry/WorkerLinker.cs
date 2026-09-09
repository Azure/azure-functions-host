// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GrpcException = Grpc.Core.RpcException;
using WorkerRpcException = Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Applies the link deadline and translates expected failures without owning worker channels.
/// </summary>
internal sealed class WorkerLinker : IWorkerLinker
{
    private readonly TimeSpan _linkTimeout;
    private readonly IWorkerChannelRegistry _registry;

    public WorkerLinker(IWorkerChannelRegistry registry)
        : this(registry, TimeSpan.FromSeconds(30))
    {
    }

    internal WorkerLinker(IWorkerChannelRegistry registry, TimeSpan linkTimeout)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(linkTimeout, TimeSpan.Zero);
        _linkTimeout = linkTimeout;
    }

    public async Task LinkAsync(string workerId, Uri grpcEndpoint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        RpcClientFactory.ValidateEndpoint(grpcEndpoint);
        cancellationToken.ThrowIfCancellationRequested();

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            Task link = _registry.LinkAsync(workerId, grpcEndpoint, deadline.Token);
            if (!link.IsCompleted)
            {
                deadline.CancelAfter(_linkTimeout);
            }

            await link;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (ObjectDisposedException exception)
        {
            throw new WorkerLinkException(WorkerLinkFailureReason.RuntimeStopping, "The runtime is stopping.", exception);
        }
        catch (Exception exception) when (exception is GrpcException or WorkerRpcException or HttpRequestException or
            IOException or SocketException or TimeoutException or ChannelClosedException or OperationCanceledException or UriFormatException)
        {
            throw new WorkerLinkException(WorkerLinkFailureReason.Unavailable,
                "The worker connection or initialization handshake was unavailable.", exception);
        }
    }
}
