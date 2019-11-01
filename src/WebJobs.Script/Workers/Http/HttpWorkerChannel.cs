// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
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
            _startLatencyMetric = metricsLogger?.LatencyEvent(string.Format(MetricEventNames.WorkerInitializeLatency, "HttpWorker", attemptCount));
        }

        public string Id { get; }

        public Task InvokeFunction(ScriptInvocationContext context)
        {
            return _httpWorkerService.InvokeAsync(context);
        }

        internal async Task<bool> DelayWorkerUntilReady()
        {
            _workerChannelLogger.LogDebug("Waiting for HttpWorker to be ready");
            try
            {
                bool isWorkerReady = await _httpWorkerService.IsWorkerReady();
                if (!isWorkerReady)
                {
                    PublishWorkerErrorEvent(new TimeoutException("Initializing HttpWorker channel timedout"));
                }
                _workerChannelLogger.LogDebug("HttpWorker is ready");
                return true;
            }
            catch (Exception ex)
            {
                // HttpFunctionInvocationDispatcher will handdle the worker error events
                PublishWorkerErrorEvent(ex);
            }
            _workerChannelLogger.LogDebug("Starting HttpWorker timedout");
            return false;
        }

        public async Task StartWorkerProcessAsync()
        {
            _workerChannelLogger.LogDebug("Initiating Worker Process start up");
            await _workerProcess.StartProcessAsync();
            await DelayWorkerUntilReady();
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
    }
}
