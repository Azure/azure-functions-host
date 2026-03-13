// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers
{
    /// <summary>
    /// Manages <see cref="ConnectedWorkerChannel"/> instances for externally-connected workers.
    /// </summary>
    internal interface IConnectedWorkerChannelManager
    {
        /// <summary>
        /// Registers a fully-initialized channel for an external worker.
        /// </summary>
        void AddChannel(string workerId, ConnectedWorkerChannel channel);

        /// <summary>
        /// Gets a specific channel by worker ID, or null if not found.
        /// </summary>
        ConnectedWorkerChannel GetChannel(string workerId);

        /// <summary>
        /// Gets all currently registered channels.
        /// </summary>
        IReadOnlyDictionary<string, ConnectedWorkerChannel> GetChannels();

        /// <summary>
        /// Shuts down and removes a specific channel, draining in-flight invocations first.
        /// </summary>
        Task ShutdownChannelAsync(string workerId);

        /// <summary>
        /// Waits until at least one fully-initialized channel is available.
        /// Used by <c>ConnectedWorkerFunctionMetadataProvider</c> to block metadata retrieval
        /// until an external worker has connected and completed the init handshake.
        /// Signaled internally when <see cref="AddChannel"/> is called.
        /// </summary>
        Task<IRpcWorkerChannel> WaitForChannelAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    }
}
