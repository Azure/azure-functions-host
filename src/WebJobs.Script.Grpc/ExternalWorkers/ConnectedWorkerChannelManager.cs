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
        private readonly ConcurrentDictionary<string, IRpcWorkerChannel> _channels = new();
        private readonly object _channelAvailableLock = new();
        private TaskCompletionSource _channelAvailable = new();

        /// <inheritdoc/>
        public void AddChannel(string workerId, IRpcWorkerChannel channel)
        {
            lock (_channelAvailableLock)
            {
                _channels[workerId] = channel;
                _channelAvailable.TrySetResult();
            }
        }

        /// <inheritdoc/>
        public IRpcWorkerChannel GetChannel(string workerId)
            => _channels.TryGetValue(workerId, out var ch) ? ch : null;

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, IRpcWorkerChannel> GetChannels()
            => _channels;

        /// <inheritdoc/>
        public async Task ShutdownChannelAsync(string workerId)
        {
            IRpcWorkerChannel channel = null;

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
                (channel as IDisposable)?.Dispose();
            }
        }

        /// <inheritdoc/>
        public async Task<IRpcWorkerChannel> WaitForChannelAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                while (true)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    Task waitTask;
                    lock (_channelAvailableLock)
                    {
                        var channel = _channels.Values.FirstOrDefault();
                        if (channel is not null)
                        {
                            return channel;
                        }

                        waitTask = _channelAvailable.Task;
                    }

                    await waitTask.WaitAsync(cts.Token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"No external worker connected within {timeout.TotalSeconds} seconds.");
            }
        }
    }
}
