// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HttpThrottleMiddlewareTests
    {
        private readonly HttpThrottleMiddleware _middleware;
        private readonly Mock<IMetricsLogger> _metricsLogger;
        private readonly Mock<HostPerformanceManager> _performanceManager;
        private readonly FunctionDescriptor _functionDescriptor;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly LoggerFactory _loggerFactory;
        private readonly Mock<IScriptJobHost> _scriptHost;
        private int _throttleMetricCount;
        private HttpRequestQueue _requestQueue;
        private HttpOptions _httpOptions;

        public HttpThrottleMiddlewareTests()
        {
            _functionDescriptor = new FunctionDescriptor("Test", null, null, new Collection<ParameterDescriptor>(), null, null, null);
            _scriptHost = new Mock<IScriptJobHost>(MockBehavior.Strict);
            _metricsLogger = new Mock<IMetricsLogger>(MockBehavior.Strict);
            _metricsLogger.Setup(p => p.LogEvent(MetricEventNames.FunctionInvokeThrottled, null, null)).Callback(() =>
            {
                Interlocked.Increment(ref _throttleMetricCount);
            });
            var environment = SystemEnvironment.Instance;
            var mockServiceProvider = new Mock<IServiceProvider>(MockBehavior.Strict);
            var healthMonitorOptions = new HostHealthMonitorOptions();
            _performanceManager = new Mock<HostPerformanceManager>(MockBehavior.Strict, environment, new OptionsWrapper<HostHealthMonitorOptions>(healthMonitorOptions), mockServiceProvider.Object, null);
            _httpOptions = new HttpOptions();
            _loggerFactory = new LoggerFactory();
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory.AddProvider(_loggerProvider);
            RequestDelegate next = (ctxt) =>
            {
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };
            _middleware = new HttpThrottleMiddleware(next, _loggerFactory, TimeSpan.FromSeconds(1));
            _requestQueue = new HttpRequestQueue(new OptionsWrapper<HttpOptions>(_httpOptions));
        }

        [Fact]
        public async Task Invoke_PropagatesExceptions()
        {
            var ex = new Exception("Kaboom!");
            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                throw ex;
            };
            var options = new HttpOptions
            {
                MaxOutstandingRequests = 10,
                MaxConcurrentRequests = 5
            };
            var middleware = new HttpThrottleMiddleware(next, _loggerFactory, TimeSpan.FromSeconds(1));

            var resultEx = await Assert.ThrowsAsync<Exception>(async () =>
            {
                var httpContext = new DefaultHttpContext();
                await middleware.Invoke(httpContext, new OptionsWrapper<HttpOptions>(_httpOptions), _requestQueue, _performanceManager.Object, _metricsLogger.Object);
            });
            Assert.True(nextInvoked);
            Assert.Same(ex, resultEx);
        }

        [Fact]
        public async Task Invoke_NoThrottle_DispatchesDirectly()
        {
            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };
            var options = new HttpOptions();
            var middleware = new HttpThrottleMiddleware(next, _loggerFactory, TimeSpan.FromSeconds(1));

            var httpContext = new DefaultHttpContext();
            await middleware.Invoke(httpContext, new OptionsWrapper<HttpOptions>(_httpOptions), _requestQueue, _performanceManager.Object, _metricsLogger.Object);
            Assert.True(nextInvoked);
            Assert.Equal(HttpStatusCode.Accepted, (HttpStatusCode)httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task Invoke_MaxParallelism_RequestsAreThrottled()
        {
            int maxParallelism = 3;
            _httpOptions = new HttpOptions
            {
                MaxConcurrentRequests = maxParallelism
            };
            _requestQueue = new HttpRequestQueue(new OptionsWrapper<HttpOptions>(_httpOptions));

            int count = 0;
            RequestDelegate next = async (ctxt) =>
            {
                if (Interlocked.Increment(ref count) > maxParallelism)
                {
                    throw new Exception($"Max parallelism of {maxParallelism} exceeded. Current parallelism: {count}");
                }

                await Task.Delay(100);
                Interlocked.Decrement(ref count);
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
            };

            var middleware = new HttpThrottleMiddleware(next, _loggerFactory, TimeSpan.FromSeconds(1));

            // expect all requests to succeed
            var tasks = new List<Task>();
            var httpContexts = new List<HttpContext>();
            for (int i = 0; i < 20; i++)
            {
                var httpContext = new DefaultHttpContext();
                httpContexts.Add(httpContext);
                tasks.Add(middleware.Invoke(httpContext, new OptionsWrapper<HttpOptions>(_httpOptions), _requestQueue, _performanceManager.Object, _metricsLogger.Object));
            }
            await Task.WhenAll(tasks);
            Assert.True(httpContexts.All(p => (HttpStatusCode)p.Response.StatusCode == HttpStatusCode.Accepted));
        }

        [Fact]
        public async Task Invoke_MaxOutstandingRequestsExceeded_RequestsAreRejected()
        {
            int maxParallelism = 1;
            int maxQueueLength = 10;
            _httpOptions = new HttpOptions
            {
                MaxOutstandingRequests = maxQueueLength,
                MaxConcurrentRequests = maxParallelism
            };
            _requestQueue = new HttpRequestQueue(new OptionsWrapper<HttpOptions>(_httpOptions));

            RequestDelegate next = async (ctxt) =>
            {
                await Task.Delay(100);
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
            };

            var middleware = new HttpThrottleMiddleware(next, _loggerFactory, TimeSpan.FromSeconds(1));

            // expect requests past the threshold to be rejected
            var tasks = new List<Task>();
            var httpContexts = new List<HttpContext>();
            for (int i = 0; i < 25; i++)
            {
                var httpContext = new DefaultHttpContext();
                httpContexts.Add(httpContext);
                tasks.Add(middleware.Invoke(httpContext, new OptionsWrapper<HttpOptions>(_httpOptions), _requestQueue, _performanceManager.Object, _metricsLogger.Object));
            }
            await Task.WhenAll(tasks);
            int countSuccess = httpContexts.Count(p => (HttpStatusCode)p.Response.StatusCode == HttpStatusCode.Accepted);
            Assert.Equal(maxQueueLength, countSuccess);
            int rejectCount = 25 - countSuccess;
            Assert.Equal(rejectCount, httpContexts.Count(p => p.Response.StatusCode == 429));

            IEnumerable<LogMessage> logMessages = _loggerProvider.GetAllLogMessages();
            Assert.Equal(rejectCount, logMessages.Count());
            Assert.True(logMessages.All(p => string.Compare("Http request queue limit of 10 has been exceeded.", p.FormattedMessage) == 0));

            // send a number of requests not exceeding the limit
            // expect all to succeed
            tasks = new List<Task>();
            httpContexts = new List<HttpContext>();
            for (int i = 0; i < maxQueueLength; i++)
            {
                var httpContext = new DefaultHttpContext();
                httpContexts.Add(httpContext);
                tasks.Add(middleware.Invoke(httpContext, new OptionsWrapper<HttpOptions>(_httpOptions), _requestQueue, _performanceManager.Object, _metricsLogger.Object));
            }
            await Task.WhenAll(tasks);
            Assert.True(httpContexts.All(p => (HttpStatusCode)p.Response.StatusCode == HttpStatusCode.Accepted));
        }

        [Fact]
        public async Task Invoke_HostIsOverloaded_RequestsAreRejected()
        {
            _httpOptions = new HttpOptions
            {
                DynamicThrottlesEnabled = true
            };
            _requestQueue = new HttpRequestQueue(new OptionsWrapper<HttpOptions>(_httpOptions));

            bool isOverloaded = false;
            _performanceManager.Setup(p => p.IsUnderHighLoadAsync(It.IsAny<Collection<string>>(), null)).Returns(() => Task.FromResult(isOverloaded));

            RequestDelegate next = async (ctxt) =>
            {
                await Task.Delay(100);
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
            };
            var middleware = new HttpThrottleMiddleware(next, _loggerFactory, TimeSpan.FromMilliseconds(50));

            var tasks = new List<Task>();
            var httpContexts = new List<HttpContext>();
            for (int i = 0; i < 10; i++)
            {
                if (i == 7)
                {
                    isOverloaded = true;
                }
                var httpContext = new DefaultHttpContext();
                httpContexts.Add(httpContext);
                await middleware.Invoke(httpContext, new OptionsWrapper<HttpOptions>(_httpOptions), _requestQueue, _performanceManager.Object, _metricsLogger.Object);
            }
            await Task.WhenAll(tasks);

            Assert.Equal(7, httpContexts.Count(p => (HttpStatusCode)p.Response.StatusCode == HttpStatusCode.Accepted));
            Assert.Equal(3, httpContexts.Count(p => (HttpStatusCode)p.Response.StatusCode == HttpStatusCode.TooManyRequests));
        }

        [Fact]
        public async Task Invoke_UnderLoad_RequestsAreRejected()
        {
            _httpOptions.DynamicThrottlesEnabled = true;

            bool highLoad = false;
            int highLoadQueryCount = 0;
            _performanceManager.Setup(p => p.IsUnderHighLoadAsync(It.IsAny<Collection<string>>(), It.IsAny<ILogger>()))
                .Callback<Collection<string>, ILogger>((exceededCounters, tw) =>
                {
                    if (highLoad)
                    {
                        exceededCounters.Add("Threads");
                        exceededCounters.Add("Processes");
                    }
                }).Returns(() =>
                {
                    highLoadQueryCount++;
                    return Task.FromResult(highLoad);
                });

            // issue some requests while not under high load
            for (int i = 0; i < 3; i++)
            {
                var httpContext = new DefaultHttpContext();
                await _middleware.Invoke(httpContext, new OptionsWrapper<HttpOptions>(_httpOptions), _requestQueue, _performanceManager.Object, _metricsLogger.Object);
                Assert.Equal(HttpStatusCode.Accepted, (HttpStatusCode)httpContext.Response.StatusCode);
                await Task.Delay(100);
            }
            Assert.Equal(1, highLoadQueryCount);
            Assert.Equal(0, _throttleMetricCount);

            // signal high load and verify requests are rejected
            await Task.Delay(1000);
            highLoad = true;
            for (int i = 0; i < 3; i++)
            {
                var httpContext = new DefaultHttpContext();
                await _middleware.Invoke(httpContext, new OptionsWrapper<HttpOptions>(_httpOptions), _requestQueue, _performanceManager.Object, _metricsLogger.Object);

                httpContext.Response.Headers.TryGetValue(ScriptConstants.AntaresScaleOutHeaderName, out StringValues values);
                string scaleOutHeader = values.Single();
                Assert.Equal("1", scaleOutHeader);
                Assert.Equal(HttpStatusCode.TooManyRequests, (HttpStatusCode)httpContext.Response.StatusCode);
                await Task.Delay(100);
            }
            Assert.Equal(2, highLoadQueryCount);
            Assert.Equal(3, _throttleMetricCount);
            var log = _loggerProvider.GetAllLogMessages().Last();
            Assert.Equal("Thresholds for the following counters have been exceeded: [Threads, Processes]", log.FormattedMessage);

            await Task.Delay(1000);
            highLoad = false;
            for (int i = 0; i < 3; i++)
            {
                var httpContext = new DefaultHttpContext();
                await _middleware.Invoke(httpContext, new OptionsWrapper<HttpOptions>(_httpOptions), _requestQueue, _performanceManager.Object, _metricsLogger.Object);
                Assert.Equal(HttpStatusCode.Accepted, (HttpStatusCode)httpContext.Response.StatusCode);
                await Task.Delay(100);
            }
            Assert.Equal(3, highLoadQueryCount);
            Assert.Equal(3, _throttleMetricCount);
        }
    }
}