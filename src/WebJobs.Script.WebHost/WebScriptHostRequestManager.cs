// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.Scaling;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostRequestManager : HttpRequestManager, ILoadFactorProvider
    {
        private readonly HostPerformanceManager _performanceManager;
        private readonly IMetricsLogger _metricsLogger;
        private readonly TraceWriter _traceWriter;
        private readonly int _performanceCheckPeriodSeconds;
        private readonly object _syncLock = new object();
        private DateTime _lastPerformanceCheck;
        private bool _rejectAllRequests;
        private int _incomingRequests;
        private int _completedRequests;
        private int _outstandingRequests;

        public WebScriptHostRequestManager(HttpExtensionConfiguration config, HostPerformanceManager performanceManager, IMetricsLogger metricsLogger, TraceWriter traceWriter, int performanceCheckPeriodSeconds = 15) : base(config, traceWriter)
        {
            _performanceManager = performanceManager;
            _metricsLogger = metricsLogger;
            _traceWriter = traceWriter;
            _performanceCheckPeriodSeconds = performanceCheckPeriodSeconds;
        }

        // TEMP
        public double GetLoadFactor()
        {
            bool shouldScale = _incomingRequests > 0;
            var loadFactor = shouldScale ? 1 : 0;

            string status = $"Http Status [Incoming: {_incomingRequests}, Completed: {_completedRequests}, Outstanding: {_outstandingRequests}]";
            var evt = new TraceEvent(System.Diagnostics.TraceLevel.Verbose, status, "Scaling");
            _traceWriter.Trace(evt);

            lock (_syncLock)
            {
                // allow the periodic calls to define the measurement windows
                _incomingRequests = 0;
                _completedRequests = 0;
            }

            return loadFactor;
        }

        /*
        public double GetLoadFactor()
        {
            var loadFactor = GetLoadFactor(_incomingRequests, _completedRequests);

            lock (_syncLock)
            {
                // allow the periodic calls to define the measurement windows
                _incomingRequests = 0;
                _completedRequests = 0;
            }

            return loadFactor;
        }

        internal static double GetLoadFactor(int incomingRequests, int completedRequests)
        {
            double requestRatio = incomingRequests;
            if (completedRequests > 0)
            {
                requestRatio = (double)incomingRequests / completedRequests;
            }

            // because a request ratio over 1 means incoming is greater than
            // completed, we want to scale out if we're ever over 1
            var loadFactor = requestRatio * (ScaleSettings.DefaultBusyWorkerLoadFactor / (double)100);

            // the max load factor we want to return is 100
            return Math.Min(loadFactor, 1);
        }
        */
        public override async Task<HttpResponseMessage> ProcessRequestAsync(HttpRequestMessage request, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> processRequestHandler, CancellationToken cancellationToken)
        {
            lock (_syncLock)
            {
                _incomingRequests++;
                _outstandingRequests++;
            }

            var response = await base.ProcessRequestAsync(request, processRequestHandler, cancellationToken);

            lock (_syncLock)
            {
                _completedRequests++;
                _outstandingRequests--;
            }

            return response;
        }

        protected override bool RejectAllRequests()
        {
            if (base.RejectAllRequests())
            {
                return true;
            }

            if (Config.DynamicThrottlesEnabled &&
               ((DateTime.UtcNow - _lastPerformanceCheck) > TimeSpan.FromSeconds(_performanceCheckPeriodSeconds)))
            {
                // only check host status periodically
                Collection<string> exceededCounters = new Collection<string>();
                _rejectAllRequests = _performanceManager.IsUnderHighLoad(exceededCounters);
                _lastPerformanceCheck = DateTime.UtcNow;
                if (_rejectAllRequests)
                {
                    TraceWriter.Warning($"Thresholds for the following counters have been exceeded: [{string.Join(", ", exceededCounters)}]");
                }
            }

            return _rejectAllRequests;
        }

        protected override HttpResponseMessage RejectRequest(HttpRequestMessage request)
        {
            var function = request.GetPropertyOrDefault<FunctionDescriptor>(ScriptConstants.AzureFunctionsHttpFunctionKey);
            _metricsLogger.LogEvent(MetricEventNames.FunctionInvokeThrottled, function.Name);

            var response = base.RejectRequest(request);
            response.Headers.Add(ScriptConstants.AntaresScaleOutHeaderName, "1");

            return response;
        }
    }
}