// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc
{
    public class HttpWorkerChannel : IHttpWorkerChannel, IDisposable
    {
        private bool _disposed;
        private IDisposable _startLatencyMetric;
        private ILogger _workerChannelLogger;
        private ILanguageWorkerProcess _languageWorkerProcess;
        private IHttpInvokerService _httpInvokerService;

        internal HttpWorkerChannel(
           string workerId,
           ILanguageWorkerProcess languageWorkerProcess,
           IHttpInvokerService httpInvokerService,
           ILogger logger,
           IMetricsLogger metricsLogger,
           int attemptCount)
        {
            Id = workerId;
            _languageWorkerProcess = languageWorkerProcess;
            _workerChannelLogger = logger;
            _httpInvokerService = httpInvokerService;
            _startLatencyMetric = metricsLogger?.LatencyEvent(string.Format(MetricEventNames.WorkerInitializeLatency, "HttpInvoker", attemptCount));
        }

        public string Id { get; }

        public Task InvokeFunction(ScriptInvocationContext context)
        {
            // TODO: convert to Http request
            return _httpInvokerService.GetInvocationResponse(context);
        }

        public async Task StartWorkerProcessAsync()
        {
            _workerChannelLogger.LogDebug("Initiating Worker Process start up");
            await _languageWorkerProcess.StartProcessAsync();
        }

        public Task Status()
        {
            // TODO: status endpoint on http invoker. Wait till 200 OK response before sending invocation requests
            throw new NotImplementedException();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _startLatencyMetric?.Dispose();

                    (_languageWorkerProcess as IDisposable)?.Dispose();
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
