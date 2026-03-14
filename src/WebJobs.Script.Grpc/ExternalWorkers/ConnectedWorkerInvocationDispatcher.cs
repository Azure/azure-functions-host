// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
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
        private int _roundRobinCounter;
        private IEnumerable<FunctionMetadata> _functions;

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
            var readyChannels = _channelManager.GetChannels().Values
                .Where(c => c.IsChannelReadyForInvocations())
                .ToList();

            if (readyChannels.Count == 0)
            {
                throw new InvalidOperationException("No connected worker channel is ready for invocations.");
            }

            IRpcWorkerChannel channel;
            if (readyChannels.Count == 1)
            {
                channel = readyChannels[0];
            }
            else
            {
                int index = Interlocked.Increment(ref _roundRobinCounter) % readyChannels.Count;
                if (_roundRobinCounter < 0 || index < 0)
                {
                    _roundRobinCounter = 0;
                    index = 0;
                }

                channel = readyChannels[index];
            }

            string functionId = invocationContext.FunctionMetadata.GetFunctionId();
            if (channel.FunctionInputBuffers.TryGetValue(functionId, out BufferBlock<ScriptInvocationContext> bufferBlock))
            {
                _logger.LogDebug("Dispatching invocation to external worker {workerId}", channel.Id);
                bufferBlock.Post(invocationContext);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Function:{invocationContext.FunctionMetadata.Name} is not loaded by the external worker: {channel.Id}");
            }

            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task InitializeAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken = default)
        {
            _functions = functions;

            foreach (var channel in _channelManager.GetChannels().Values)
            {
                SetupChannel(channel);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Sets up function invocation buffers and sends function load requests on a channel.
        /// Called during <see cref="InitializeAsync"/> for existing channels, and can be called
        /// when new channels connect after initialization.
        /// </summary>
        internal void SetupChannel(IRpcWorkerChannel channel)
        {
            if (_functions is null)
            {
                return;
            }

            channel.SetupFunctionInvocationBuffers(_functions);
            channel.SendFunctionLoadRequests(managedDependencyOptions: null, functionTimeout: null);
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
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public Task StartWorkerChannel()
        {
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
