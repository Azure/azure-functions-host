// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.WorkerProxy.Logging;

/// <summary>
/// Formats and queues language-worker system logs for platform ingestion.
/// </summary>
internal interface IWorkerSystemLogSink
{
    /// <summary>
    /// Attempts to format and enqueue a worker system log.
    /// </summary>
    /// <param name="rpcLog">The worker log to emit.</param>
    /// <returns>The queue admission result.</returns>
    WorkerSystemLogEnqueueResult TryEmit(RpcLog rpcLog);
}
