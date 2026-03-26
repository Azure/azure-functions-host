// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers
{
    /// <summary>
    /// Invocation dispatcher for external (separately-hosted) workers.
    /// Only does routing — no process management, no restart logic.
    /// Registered as <see cref="IFunctionInvocationDispatcher"/> in external worker mode.
    /// </summary>
    internal class ConnectedWorkerInvocationDispatcher : FunctionInvocationDispatcher
    {
        private readonly IConnectedWorkerChannelManager _channelManager;
        private IEnumerable<FunctionMetadata> _functions;

        public ConnectedWorkerInvocationDispatcher(
            IConnectedWorkerChannelManager channelManager,
            IRpcFunctionInvocationDispatcherLoadBalancer loadBalancer,
            IOptions<ScriptJobHostOptions> scriptJobHostOptions,
            ILogger<ConnectedWorkerInvocationDispatcher> logger)
            : base(loadBalancer, scriptJobHostOptions, logger)
        {
            _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
            State = FunctionInvocationDispatcherState.Default;
            ErrorEventsThreshold = 3;
        }

        /// <inheritdoc/>
        protected override Task<IEnumerable<IRpcWorkerChannel>> GetReadyChannelsAsync(ScriptInvocationContext invocationContext)
        {
            IEnumerable<IRpcWorkerChannel> channels = _channelManager.GetChannels().Values
                .Where(c => c.IsChannelReadyForInvocations());

            return Task.FromResult(channels);
        }

        /// <inheritdoc/>
        public override Task InitializeAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (functions is null || !functions.Any())
            {
                Logger.LogDebug($"{nameof(ConnectedWorkerInvocationDispatcher)} received no functions");
                return Task.CompletedTask;
            }

            State = FunctionInvocationDispatcherState.Initializing;

            _functions = functions;

            foreach (var channel in _channelManager.GetChannels().Values)
            {
                SetupChannel(channel);
            }

            AddLogUserCategory(functions);

            State = FunctionInvocationDispatcherState.Initialized;

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
        public override async Task<IDictionary<string, WorkerStatus>> GetWorkerStatusesAsync()
        {
            var result = new Dictionary<string, WorkerStatus>();
            foreach (var (id, channel) in _channelManager.GetChannels())
            {
                result[id] = await channel.GetWorkerStatusAsync();
            }

            return result;
        }

        /// <inheritdoc/>
        public override Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception)
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public override Task StartWorkerChannel()
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override void PreShutdown()
        {
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            State = FunctionInvocationDispatcherState.Disposing;
            State = FunctionInvocationDispatcherState.Disposed;
        }
    }
}