// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Host.Executors.Internal;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    /// <summary>
    /// Abstract base class for function invocation dispatchers. Provides the shared
    /// invoke path: system scope → channel selection (round-robin) → buffer post,
    /// and the shared shutdown path: drain invocations with timeout.
    /// Subclasses implement <see cref="GetReadyChannelsAsync"/> to resolve channels
    /// from their respective channel managers.
    /// </summary>
    internal abstract class FunctionInvocationDispatcher : IFunctionInvocationDispatcher
    {
        private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(10);

        private readonly IRpcFunctionInvocationDispatcherLoadBalancer _loadBalancer;
        private readonly IOptions<ScriptJobHostOptions> _scriptJobHostOptions;

        protected FunctionInvocationDispatcher(
            IRpcFunctionInvocationDispatcherLoadBalancer loadBalancer,
            IOptions<ScriptJobHostOptions> scriptJobHostOptions,
            ILogger logger)
        {
            _loadBalancer = loadBalancer ?? throw new ArgumentNullException(nameof(loadBalancer));
            _scriptJobHostOptions = scriptJobHostOptions ?? throw new ArgumentNullException(nameof(scriptJobHostOptions));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected ILogger Logger { get; }

        /// <summary>
        /// Gets the timeout for draining invocations during shutdown.
        /// Subclasses can override to use a configured value.
        /// </summary>
        protected virtual TimeSpan ShutdownTimeout => DefaultShutdownTimeout;

        /// <inheritdoc/>
        public virtual FunctionInvocationDispatcherState State { get; protected set; }

        /// <inheritdoc/>
        public virtual int ErrorEventsThreshold { get; protected set; }

        /// <inheritdoc/>
        public async Task InvokeAsync(ScriptInvocationContext invocationContext)
        {
            using FunctionInvoker.Scope scope = FunctionInvoker.BeginSystemScope();

            IEnumerable<IRpcWorkerChannel> workerChannels = await GetReadyChannelsAsync(invocationContext);
            var channel = _loadBalancer.GetLanguageWorkerChannel(workerChannels);
            string functionId = invocationContext.FunctionMetadata.GetFunctionId();

            if (channel.FunctionInputBuffers.TryGetValue(functionId, out BufferBlock<ScriptInvocationContext> bufferBlock))
            {
                if (Logger.IsEnabled(LogLevel.Trace))
                {
                    Logger.LogTrace("Posting invocation id:{InvocationId} on workerId:{workerChannelId}",
                        invocationContext.ExecutionContext.InvocationId, channel.Id);
                }

                bufferBlock.Post(invocationContext);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Function:{invocationContext.FunctionMetadata.Name} is not loaded by the worker: {channel.Id}");
            }
        }

        /// <inheritdoc/>
        public virtual async Task ShutdownAsync()
        {
            Logger.LogDebug("Waiting for {dispatcher} to shutdown", GetType().Name);

            var channels = await GetReadyChannelsAsync(invocationContext: null);
            var drainTasks = channels.Select(c => c.DrainInvocationsAsync()).ToList();

            if (drainTasks.Count > 0)
            {
                Task drainAll = Task.WhenAll(drainTasks);
                Task timeout = Task.Delay(ShutdownTimeout);
                Task completedTask = await Task.WhenAny(drainAll, timeout);

                if (completedTask.Equals(timeout))
                {
                    Logger.LogDebug("Draining invocations from worker channels timed out during shutdown");
                }
                else
                {
                    Logger.LogDebug("Draining invocations from worker channels completed during shutdown");
                }
            }
        }

        /// <summary>
        /// Resolves the set of ready worker channels.
        /// Each subclass resolves channels from its own channel manager.
        /// </summary>
        /// <param name="invocationContext">
        /// The invocation context, used by subclasses that need per-function language routing.
        /// May be <see langword="null"/> when called outside of invocation (e.g., during shutdown).
        /// </param>
        protected abstract Task<IEnumerable<IRpcWorkerChannel>> GetReadyChannelsAsync(ScriptInvocationContext invocationContext);

        /// <inheritdoc/>
        public abstract Task InitializeAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken = default);

        /// <inheritdoc/>
        public abstract Task<IDictionary<string, WorkerStatus>> GetWorkerStatusesAsync();

        /// <inheritdoc/>
        public abstract Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception);

        /// <inheritdoc/>
        public abstract Task StartWorkerChannel();

        /// <inheritdoc/>
        public abstract void PreShutdown();

        /// <inheritdoc/>
        public abstract void Dispose();

        /// <summary>
        /// Enriches function metadata with log correlation properties.
        /// Must be called during <see cref="InitializeAsync"/> to ensure proper log categorization.
        /// </summary>
        protected void AddLogUserCategory(IEnumerable<FunctionMetadata> functions)
        {
            foreach (FunctionMetadata metadata in functions)
            {
                metadata.Properties[LogConstants.CategoryNameKey] = LogCategories.CreateFunctionUserCategory(metadata.Name);
                metadata.Properties[ScriptConstants.LogPropertyHostInstanceIdKey] = _scriptJobHostOptions.Value.InstanceId;
            }
        }
    }
}
