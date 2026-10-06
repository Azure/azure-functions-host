// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.WorkerProxy.Logging;

/// <summary>
/// Emits language-worker system logs for platform ingestion.
/// </summary>
internal interface IWorkerSystemLogSink
{
    /// <summary>
    /// Emits a worker system log.
    /// </summary>
    /// <param name="rpcLog">The worker log to emit.</param>
    /// <param name="cancellationToken">A token that cancels the emission.</param>
    /// <returns>A task that represents the emission.</returns>
    ValueTask EmitAsync(RpcLog rpcLog, CancellationToken cancellationToken);
}
