// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal class GrpcWorkerChannel : WorkerChannel
    {
        private IWorkerProcess _rpcWorkerProcess;

        internal GrpcWorkerChannel(
            string workerId,
            IScriptEventManager eventManager,
            IScriptHostManager hostManager,
            RpcWorkerConfig workerConfig,
            IWorkerProcess rpcWorkerProcess,
            ILogger logger,
            IMetricsLogger metricsLogger,
            int attemptCount,
            IEnvironment environment,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
            ISharedMemoryManager sharedMemoryManager,
            IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions,
            IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
            IAppCapabilitiesStore appCapabilitiesStore,
            IHttpProxyService httpProxyService)
            : base(
                workerId,
                eventManager,
                hostManager,
                workerConfig,
                logger,
                metricsLogger,
                attemptCount,
                environment,
                applicationHostOptions,
                sharedMemoryManager,
                workerConcurrencyOptions,
                hostingConfigOptions,
                appCapabilitiesStore,
                httpProxyService)
        {
            _rpcWorkerProcess = rpcWorkerProcess;

            EventSubscriptions.Add(EventManager.OfType<FileEvent>()
                .Where(msg => WorkerConfig.Description.Extensions.Contains(Path.GetExtension(msg.FileChangeArguments.FullPath)))
                .Throttle(TimeSpan.FromMilliseconds(300)) // debounce
                .Subscribe(msg => EventManager.Publish(new HostRestartEvent($"Worker monitored file changed: '{msg.FileChangeArguments.Name}'."))));
        }

        public override IWorkerProcess WorkerProcess => _rpcWorkerProcess;

        protected override int WorkerProcessId => _rpcWorkerProcess.Id;

        public override async Task StartWorkerProcessAsync(CancellationToken cancellationToken)
        {
            BeginInboundProcessing(WorkerConfig.CountOptions.ProcessStartupTimeout);

            WorkerChannelLogger.LogDebug("Initiating Worker Process start up");
            await _rpcWorkerProcess.StartProcessAsync(cancellationToken);
            State |= RpcWorkerChannelState.Initializing;
            Task<int> exited = _rpcWorkerProcess.WaitForExitAsync(cancellationToken);
            Task winner = await Task.WhenAny(WorkerInitTask.Task, exited).WaitAsync(cancellationToken);
            await winner;

            if (winner == exited)
            {
                throw new WorkerProcessExitException("Worker process exited before initializing.")
                {
                    ExitCode = await exited,
                };
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopWorkerProcess();
            }

            base.Dispose(disposing);
        }

        protected override void DisposeWorkerResources()
        {
            (_rpcWorkerProcess as IDisposable)?.Dispose();
        }

        private void StopWorkerProcess()
        {
            bool capabilityEnabled = !string.IsNullOrEmpty(WorkerCapabilities.GetCapabilityState(RpcWorkerConstants.HandlesWorkerTerminateMessage));
            if (!capabilityEnabled)
            {
                return;
            }

            int gracePeriod = WorkerConstants.WorkerTerminateGracePeriodInSeconds;

            var workerTerminate = new WorkerTerminate()
            {
                GracePeriod = Duration.FromTimeSpan(TimeSpan.FromSeconds(gracePeriod))
            };

            WorkerChannelLogger.LogDebug("Sending WorkerTerminate message with grace period of {gracePeriod} seconds.", gracePeriod);

            SendStreamingMessage(new StreamingMessage
            {
                WorkerTerminate = workerTerminate
            });

            WorkerProcess.WaitForProcessExitInMilliSeconds(gracePeriod * 1000);
        }
    }
}