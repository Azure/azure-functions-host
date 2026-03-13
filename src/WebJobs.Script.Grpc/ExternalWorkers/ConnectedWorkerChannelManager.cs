// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers
{
    /// <summary>
    /// Manages <see cref="ConnectedWorkerChannel"/> instances for externally-connected workers.
    /// Thread-safe. Supports blocking waits for the first available channel.
    /// </summary>
    internal class ConnectedWorkerChannelManager : IConnectedWorkerChannelManager
    {
        private readonly ConcurrentDictionary<string, ConnectedWorkerChannel> _channels = new();
        private TaskCompletionSource<IRpcWorkerChannel> _firstChannelReady = new();

        /// <inheritdoc/>
        public void AddChannel(string workerId, ConnectedWorkerChannel channel)
        {
            _channels[workerId] = channel;
            _firstChannelReady.TrySetResult(channel);
        }

        /// <inheritdoc/>
        public ConnectedWorkerChannel GetChannel(string workerId)
            => _channels.TryGetValue(workerId, out var ch) ? ch : null;

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, ConnectedWorkerChannel> GetChannels()
            => _channels;

        /// <inheritdoc/>
        public async Task ShutdownChannelAsync(string workerId)
        {
            if (_channels.TryRemove(workerId, out var channel))
            {
                await channel.DrainInvocationsAsync();
                channel.Dispose();
            }
        }

        /// <inheritdoc/>
        public async Task<IRpcWorkerChannel> WaitForChannelAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var ready = _channels.Values.FirstOrDefault(c => c.IsChannelReadyForInvocations());
            if (ready is not null)
            {
                return ready;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                return await _firstChannelReady.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"No external worker connected within {timeout.TotalSeconds} seconds.");
            }
        }
    }
}
