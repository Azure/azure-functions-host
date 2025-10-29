// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public class HttpWorkerChannel : IHttpWorkerChannel, IDisposable
    {
        private readonly IScriptEventManager _eventManager;
        private bool _disposed;
        private IDisposable _startLatencyMetric;
        private ILogger _workerChannelLogger;
        private IWorkerProcess _workerProcess;
        private IHttpWorkerService _httpWorkerService;
        private IMetricsLogger _metricsLogger;

        internal HttpWorkerChannel(
           string workerId,
           IScriptEventManager eventManager,
           IWorkerProcess rpcWorkerProcess,
           IHttpWorkerService httpWorkerService,
           ILogger logger,
           IMetricsLogger metricsLogger,
           int attemptCount)
        {
            Id = workerId;
            _eventManager = eventManager;
            _workerProcess = rpcWorkerProcess;
            _workerChannelLogger = logger;
            _httpWorkerService = httpWorkerService;
            _metricsLogger = metricsLogger;
            _startLatencyMetric = metricsLogger?.LatencyEvent(string.Format(MetricEventNames.WorkerInitializeLatency, "HttpWorker", attemptCount));
        }

        public string Id { get; }

        public IWorkerProcess WorkerProcess => _workerProcess;

        public Task InvokeAsync(ScriptInvocationContext context)
        {
            return _httpWorkerService.InvokeAsync(context);
        }

        internal async Task DelayUntilWokerInitialized(CancellationToken cancellationToken)
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.DelayUntilWorkerIsInitialized))
            {
                _workerChannelLogger.LogDebug("Initializing HttpWorker.");
                try
                {
                    bool isWorkerReady = await _httpWorkerService.IsWorkerReady(cancellationToken);
                    if (!isWorkerReady)
                    {
                        throw new TimeoutException("Initializing HttpWorker timed out.");
                    }
                    else
                    {
                        _workerChannelLogger.LogDebug("HttpWorker is Initialized.");
                    }
                }
                catch (Exception ex)
                {
                    // HttpFunctionInvocationDispatcher will handdle the worker error events
                    _workerChannelLogger.LogError(ex, "Failed to start http worker process. workerId:{id}", Id);
                    PublishWorkerErrorEvent(ex);
                    throw;
                }
            }
        }

        public async Task StartWorkerProcessAsync(CancellationToken cancellationToken)
        {
            _workerChannelLogger.LogDebug("Initiating Worker Process start up");
            await _workerProcess.StartProcessAsync();
            await DelayUntilWokerInitialized(cancellationToken);
        }

        private void PublishWorkerErrorEvent(Exception exc)
        {
            if (_disposed)
            {
                return;
            }
            _eventManager.Publish(new HttpWorkerErrorEvent(Id, exc));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _startLatencyMetric?.Dispose();

                    (_workerProcess as IDisposable)?.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public async Task<WorkerStatus> GetWorkerStatusAsync()
        {
            var sw = ValueStopwatch.StartNew();
            await _httpWorkerService.PingAsync();
            var elapsed = sw.GetElapsedTime();
            var workerStatus = new WorkerStatus
            {
                Latency = elapsed
            };
            _workerChannelLogger.LogDebug($"[HostMonitor] Worker status request took {elapsed.TotalMilliseconds}ms");
            return workerStatus;
        }
    }
}
