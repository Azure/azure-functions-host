// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers
{
    /// <summary>
    /// Invocation dispatcher for external (separately-hosted) workers.
    /// Only does routing — no process management, no restart logic.
    /// Registered as <see cref="IFunctionInvocationDispatcher"/> in external worker mode.
    /// </summary>
    internal class ConnectedWorkerInvocationDispatcher : IFunctionInvocationDispatcher
    {
        private readonly IConnectedWorkerChannelManager _channelManager;
        private readonly ILogger<ConnectedWorkerInvocationDispatcher> _logger;

        public ConnectedWorkerInvocationDispatcher(
            IConnectedWorkerChannelManager channelManager,
            ILogger<ConnectedWorkerInvocationDispatcher> logger)
        {
            _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public FunctionInvocationDispatcherState State
        {
            get
            {
                bool anyReady = _channelManager.GetChannels().Values
                    .Any(c => c.IsChannelReadyForInvocations());

                return anyReady
                    ? FunctionInvocationDispatcherState.Initialized
                    : FunctionInvocationDispatcherState.Initializing;
            }
        }

        /// <inheritdoc/>
        public int ErrorEventsThreshold => int.MaxValue;

        /// <inheritdoc/>
        public async Task InvokeAsync(ScriptInvocationContext invocationContext)
        {
            var channels = _channelManager.GetChannels();
            var channel = channels.Values
                .FirstOrDefault(c => c.IsChannelReadyForInvocations());

            if (channel is null)
            {
                throw new InvalidOperationException("No connected worker channel is ready for invocations.");
            }

            _logger.LogDebug("Dispatching invocation to external worker {workerId}", channel.Id);
            await channel.SendInvocationRequest(invocationContext);
        }

        /// <inheritdoc/>
        public Task InitializeAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken = default)
        {
            // No-op: external workers provide their own metadata and are already connected.
            // Function load requests will be sent when the channel is ready.
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task<IDictionary<string, WorkerStatus>> GetWorkerStatusesAsync()
        {
            var result = new Dictionary<string, WorkerStatus>();
            foreach (var (id, channel) in _channelManager.GetChannels())
            {
                result[id] = await channel.GetWorkerStatusAsync();
            }

            return result;
        }

        /// <inheritdoc/>
        public Task ShutdownAsync()
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception)
        {
            // External workers cannot be restarted by the host.
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public Task StartWorkerChannel()
        {
            // No-op: workers connect inbound; the host does not start them.
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public void PreShutdown()
        {
        }

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }
}
