// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#if WEBREQUESTMANAGER
using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Scale;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostRequestManager : HttpRequestManager
    {
        private readonly HostPerformanceManager _performanceManager;
        private readonly IMetricsLogger _metricsLogger;
        private readonly int _performanceCheckPeriodSeconds;
        private DateTime _lastPerformanceCheck;
        private bool _rejectAllRequests;

        public WebScriptHostRequestManager(HttpExtensionConfiguration config, HostPerformanceManager performanceManager, IMetricsLogger metricsLogger, TraceWriter traceWriter, int performanceCheckPeriodSeconds = 15) : base(config, traceWriter)
        {
            _performanceManager = performanceManager;
            _metricsLogger = metricsLogger;
            _performanceCheckPeriodSeconds = performanceCheckPeriodSeconds;
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

            return base.RejectRequest(request);
        }
    }
}
#endif