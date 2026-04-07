// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Factory for creating <see cref="IConnectedWorkerChannel"/> instances.
/// </summary>
internal interface IConnectedWorkerChannelFactory
{
    /// <summary>
    /// Creates a new channel for an externally-connected worker.
    /// </summary>
    IConnectedWorkerChannel Create(string workerId, RpcWorkerConfig workerConfig);
}
