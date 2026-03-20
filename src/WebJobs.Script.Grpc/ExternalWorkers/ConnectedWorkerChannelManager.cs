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
        private readonly object _channelAvailableLock = new();
        private TaskCompletionSource _channelAvailable = new();

        /// <inheritdoc/>
        public void AddChannel(string workerId, ConnectedWorkerChannel channel)
        {
            lock (_channelAvailableLock)
            {
                _channels[workerId] = channel;
                _channelAvailable.TrySetResult();
            }
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
            ConnectedWorkerChannel channel = null;

            lock (_channelAvailableLock)
            {
                _channels.TryRemove(workerId, out channel);

                if (channel is not null && _channels.IsEmpty)
                {
                    _channelAvailable = new TaskCompletionSource();
                }
            }

            if (channel is not null)
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
                await _channelAvailable.Task.WaitAsync(cts.Token);

                return _channels.Values.FirstOrDefault(c => c.IsChannelReadyForInvocations())
                    ?? _channels.Values.First();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"No external worker connected within {timeout.TotalSeconds} seconds.");
            }
        }
    }
}
