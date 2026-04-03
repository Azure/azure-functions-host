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
    /// Waits for the worker to complete the init handshake.
    /// </summary>
    Task WaitForInitAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the value of a capability reported during initialization,
    /// or <see langword="null"/> if not present.
    /// </summary>
    string GetCapabilityState(string capability);
}
