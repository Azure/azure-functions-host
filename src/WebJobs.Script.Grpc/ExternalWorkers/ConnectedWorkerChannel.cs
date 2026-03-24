// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers
{
    /// <summary>
    /// A worker channel for a worker that connected inbound — the host did not spawn a process.
    /// Lifecycle: the gRPC stream IS the lifecycle; disconnection → WorkerErrorEvent.
    /// </summary>
    internal sealed class ConnectedWorkerChannel : WorkerChannel
    {
        internal ConnectedWorkerChannel(
            string workerId,
            IScriptEventManager eventManager,
            IScriptHostManager hostManager,
            RpcWorkerConfig workerConfig,
            ILogger logger,
            IMetricsLogger metricsLogger,
            IEnvironment environment,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
            ISharedMemoryManager sharedMemoryManager,
            IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions,
            IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
            IHttpProxyService httpProxyService)
            : base(workerId, eventManager, hostManager, workerConfig, logger, metricsLogger,
                   attemptCount: 0, environment, applicationHostOptions, sharedMemoryManager,
                   workerConcurrencyOptions, hostingConfigOptions, httpProxyService)
        {
            WorkerInitTask.Task.ContinueWith(
                t =>
                {
                    if (t.Result)
                    {
                        EventManager.Publish(new WorkerConnectedEvent(Id, WorkerConfig.Description.Language));
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        /// <inheritdoc/>
        public override IWorkerProcess WorkerProcess => null;

        /// <inheritdoc/>
        public override Task StartWorkerProcessAsync(CancellationToken cancellationToken)
        {
            BeginInboundProcessing(startStreamTimeout: TimeSpan.FromSeconds(30));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Waits for the worker to complete the WorkerInitResponse handshake.
        /// </summary>
        /// <param name="timeout">Maximum time to wait for initialization.</param>
        /// <param name="cancellationToken">Token to cancel the wait.</param>
        /// <returns>A task that completes when the worker has initialized.</returns>
        public Task WaitForInitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            return WorkerInitTask.Task.WaitAsync(timeout, cancellationToken);
        }

        /// <summary>
        /// Returns the value of the specified capability reported by the worker during initialization,
        /// or <see langword="null"/> if the capability was not present.
        /// </summary>
        /// <param name="capability">The capability key to look up.</param>
        /// <returns>The capability value, or <see langword="null"/>.</returns>
        public string GetCapabilityState(string capability) => WorkerCapabilities.GetCapabilityState(capability);

        /// <inheritdoc/>
        protected override void DisposeWorkerResources()
        {
        }
    }
}
