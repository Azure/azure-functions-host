// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public class HttpWorkerChannel : IHttpWorkerChannel, IDisposable
    {
        private bool _disposed;
        private IDisposable _startLatencyMetric;
        private ILogger _workerChannelLogger;
        private IWorkerProcess _rpcWorkerProcess;
        private IHttpWorkerService _httpWorkerService;

        internal HttpWorkerChannel(
           string workerId,
           IWorkerProcess rpcWorkerProcess,
           IHttpWorkerService httpWorkerService,
           ILogger logger,
           IMetricsLogger metricsLogger,
           int attemptCount)
        {
            Id = workerId;
            _rpcWorkerProcess = rpcWorkerProcess;
            _workerChannelLogger = logger;
            _httpWorkerService = httpWorkerService;
            _startLatencyMetric = metricsLogger?.LatencyEvent(string.Format(MetricEventNames.WorkerInitializeLatency, "HttpWorker", attemptCount));
        }

        public string Id { get; }

        public Task InvokeFunction(ScriptInvocationContext context)
        {
            return _httpWorkerService.InvokeAsync(context);
        }

        public async Task StartWorkerProcessAsync()
        {
            _workerChannelLogger.LogDebug("Initiating Worker Process start up");
            await _rpcWorkerProcess.StartProcessAsync();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _startLatencyMetric?.Dispose();

                    (_rpcWorkerProcess as IDisposable)?.Dispose();
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
