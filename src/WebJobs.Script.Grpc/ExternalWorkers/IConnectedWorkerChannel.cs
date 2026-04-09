// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Represents a channel to an externally-connected worker. Extends <see cref="IRpcWorkerChannel"/>
/// with the handshake operations needed by <see cref="WorkerConnectionService"/>.
/// </summary>
internal interface IConnectedWorkerChannel : IRpcWorkerChannel
{
    /// <summary>
    /// Raised when the worker proxy sends a <c>WorkerDrainRequest</c> over gRPC.
    /// The subscriber should drain in-flight invocations and send <c>WorkerDrainComplete</c> back.
    /// </summary>
    event Action<string> DrainRequested;

    /// <summary>
    /// Gets a value indicating whether this channel is currently draining.
    /// </summary>
    bool IsDraining { get; }

    /// <summary>
    /// Waits for the worker to complete the init handshake.
    /// </summary>
    Task WaitForInitAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the value of a capability reported during initialization,
    /// or <see langword="null"/> if not present.
    /// </summary>
    string GetCapabilityState(string capability);

    /// <summary>
    /// Marks this channel as draining. New invocations will not be routed to this channel,
    /// but in-flight invocations will continue to completion.
    /// </summary>
    void BeginDrain();

    /// <summary>
    /// Sends a <c>WorkerDrainComplete</c> message to the worker proxy over gRPC.
    /// </summary>
    void SendWorkerDrainComplete();

    /// <summary>
    /// Sends a <c>WorkerDrainRequest</c> message to the worker proxy over gRPC.
    /// Used in runtime-initiated stop to notify the proxy it should
    /// enter the <c>Draining</c> state. Idempotent — safe to send even if the proxy
    /// already initiated the drain.
    /// </summary>
    void SendWorkerDrainRequest();
}
