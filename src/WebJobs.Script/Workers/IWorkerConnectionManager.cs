// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Workers;

/// <summary>
/// Manages outbound connections to externally-hosted workers.
/// Supports dynamic worker allocation and deallocation via platform APIs.
/// </summary>
public interface IWorkerConnectionManager
{
    /// <summary>
    /// Gets the count of workers in the <see cref="WorkerConnectionState.Connected"/> state.
    /// </summary>
    int ActiveWorkerCount { get; }

    /// <summary>
    /// Connects to an external worker at the specified gRPC endpoint.
    /// Creates the outbound gRPC connection, performs the init handshake,
    /// and registers the channel. For subsequent workers (after the host is
    /// already initialized), loads existing functions onto the new channel.
    /// </summary>
    /// <param name="workerId">Platform-assigned worker identifier.</param>
    /// <param name="endpoint">The gRPC endpoint URI of the worker sidecar.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConnectWorkerAsync(string workerId, Uri endpoint, CancellationToken cancellationToken);

    /// <summary>
    /// Connects to an external worker and overrides the HTTP proxy endpoint used
    /// for HTTP trigger forwarding.
    /// </summary>
    /// <param name="workerId">Platform-assigned worker identifier.</param>
    /// <param name="endpoint">The gRPC endpoint URI of the worker sidecar.</param>
    /// <param name="workerHttpEndpoint">The platform-routable HTTP endpoint of the worker sidecar, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConnectWorkerAsync(string workerId, Uri endpoint, Uri workerHttpEndpoint, CancellationToken cancellationToken);

    /// <summary>
    /// Disconnects an external worker. Drains in-flight invocations,
    /// closes the gRPC connection, and removes the channel.
    /// </summary>
    /// <param name="workerId">The worker identifier to disconnect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DisconnectWorkerAsync(string workerId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the connection status for all tracked workers.
    /// </summary>
    IReadOnlyList<WorkerConnectionInfo> GetWorkerStatuses();

    /// <summary>
    /// Gets the connection status for a specific worker, or <see langword="null"/> if not found.
    /// </summary>
    /// <param name="workerId">The worker identifier.</param>
    WorkerConnectionInfo GetWorkerStatus(string workerId);

    /// <summary>
    /// Drains and disconnects all connected workers in parallel.
    /// Each worker is drained (in-flight invocations complete with timeout),
    /// sent a <c>WorkerDrainComplete</c> message, and then the gRPC connection is closed.
    /// Used by the <c>/admin/instance/stop</c> endpoint for runtime-initiated stop.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DrainAndDisconnectAllAsync(CancellationToken cancellationToken);
}
