// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    internal class HttpThrottleMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly TimeSpan? _performanceCheckInterval;
        private readonly ILogger _logger;
        private DateTime _lastPerformanceCheck;
        private bool _rejectRequests;

        public HttpThrottleMiddleware(RequestDelegate next, ILoggerFactory loggerFactory, TimeSpan? performanceCheckInterval = null)
        {
            _next = next;
            _performanceCheckInterval = performanceCheckInterval ?? TimeSpan.FromSeconds(15);
            _logger = loggerFactory?.CreateLogger("Host." + ScriptConstants.TraceSourceHttpThrottleMiddleware);
        }

        public static bool ShouldEnable(HttpOptions options)
        {
            return HttpRequestQueue.IsEnabled(options) || options.DynamicThrottlesEnabled;
        }

        public async Task Invoke(HttpContext httpContext, IOptions<HttpOptions> httpOptions, HttpRequestQueue requestQueue, HostPerformanceManager performanceManager, IMetricsLogger metricsLogger)
        {
            if (httpOptions.Value.DynamicThrottlesEnabled &&
               ((DateTime.UtcNow - _lastPerformanceCheck) > _performanceCheckInterval))
            {
                // only check host status periodically
                Collection<string> exceededCounters = new Collection<string>();
                _rejectRequests = await performanceManager.IsUnderHighLoadAsync(exceededCounters);
                _lastPerformanceCheck = DateTime.UtcNow;
                if (_rejectRequests)
                {
                    _logger.LogWarning($"Thresholds for the following counters have been exceeded: [{string.Join(", ", exceededCounters)}]");
                }
            }

            if (_rejectRequests)
            {
                // we're currently in reject mode, so reject the request and
                // call the next delegate without calling base
                RejectRequest(httpContext, metricsLogger);
                return;
            }

            if (requestQueue.Enabled)
            {
                var success = await requestQueue.Post(httpContext, _next);
                if (!success)
                {
                    _logger?.LogInformation($"Http request queue limit of {httpOptions.Value.MaxOutstandingRequests} has been exceeded.");
                    RejectRequest(httpContext, metricsLogger);
                }
            }
            else
            {
                // queue is not enabled, so just dispatch the request directly
                await _next.Invoke(httpContext);
            }
        }

        private void RejectRequest(HttpContext httpContext, IMetricsLogger metricsLogger)
        {
            metricsLogger.LogEvent(MetricEventNames.FunctionInvokeThrottled);

            httpContext.Response.StatusCode = 429;
            httpContext.Response.Headers.Add(ScriptConstants.AntaresScaleOutHeaderName, "1");
        }
    }
}
