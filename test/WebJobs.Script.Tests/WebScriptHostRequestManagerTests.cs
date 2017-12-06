// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#if WEBREQUESTMANAGER
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebScriptHostRequestManagerTests
    {
        private readonly WebScriptHostRequestManager _requestManager;
        private readonly Mock<IMetricsLogger> _metricsLogger;
        private readonly Mock<HostPerformanceManager> _performanceManager;
        private readonly FunctionDescriptor _functionDescriptor;
        private readonly HttpExtensionConfiguration _httpConfig;
        private readonly TestTraceWriter _traceWriter;

        public WebScriptHostRequestManagerTests()
        {
            _metricsLogger = new Mock<IMetricsLogger>(MockBehavior.Strict);
            _performanceManager = new Mock<HostPerformanceManager>(MockBehavior.Strict, new object[] { new ScriptSettingsManager(), new HostHealthMonitorConfiguration() });
            _httpConfig = new HttpExtensionConfiguration();
            _traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            _requestManager = new WebScriptHostRequestManager(_httpConfig, _performanceManager.Object, _metricsLogger.Object, _traceWriter, 1);
            _functionDescriptor = new FunctionDescriptor("Test", null, null, new Collection<ParameterDescriptor>(), null, null, null);
        }

        [Fact]
        public async Task ProcessRequestAsync_PerformanceThrottle_ReturnsExpectedResult()
        {
            _httpConfig.DynamicThrottlesEnabled = true;

            bool highLoad = false;
            int highLoadQueryCount = 0;
            _performanceManager.Setup(p => p.IsUnderHighLoad(It.IsAny<Collection<string>>()))
                .Callback<Collection<string>, TraceWriter>((exceededCounters, tw) =>
                {
                    if (highLoad)
                    {
                        exceededCounters.Add("Threads");
                        exceededCounters.Add("Processes");
                    }
                }).Returns(() =>
                {
                    highLoadQueryCount++;
                    return highLoad;
                });
            int throttleMetricCount = 0;
            _metricsLogger.Setup(p => p.LogEvent(MetricEventNames.FunctionInvokeThrottled, "Test")).Callback(() =>
            {
                throttleMetricCount++;
            });

            // issue some requests while not under high load
            var request = new HttpRequestMessage();
            HttpResponseMessage response = null;
            for (int i = 0; i < 3; i++)
            {
                response = await _requestManager.ProcessRequestAsync(request, ProcessRequest, CancellationToken.None);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                await Task.Delay(100);
            }
            Assert.Equal(1, highLoadQueryCount);
            Assert.Equal(0, throttleMetricCount);

            // signal high load and verify requests are rejected
            await Task.Delay(1000);
            highLoad = true;
            for (int i = 0; i < 3; i++)
            {
                response = await _requestManager.ProcessRequestAsync(request, ProcessRequest, CancellationToken.None);
                var scaleOutHeader = response.Headers.GetValues(ScriptConstants.AntaresScaleOutHeaderName).Single();
                Assert.Equal("1", scaleOutHeader);
                Assert.Equal((HttpStatusCode)429, response.StatusCode);
                await Task.Delay(100);
            }
            Assert.Equal(2, highLoadQueryCount);
            Assert.Equal(3, throttleMetricCount);
            var trace = _traceWriter.Traces.Last();
            Assert.Equal("Thresholds for the following counters have been exceeded: [Threads, Processes]", trace.Message);

            await Task.Delay(1000);
            highLoad = false;
            for (int i = 0; i < 3; i++)
            {
                response = await _requestManager.ProcessRequestAsync(request, ProcessRequest, CancellationToken.None);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                await Task.Delay(100);
            }
            Assert.Equal(3, highLoadQueryCount);
            Assert.Equal(3, throttleMetricCount);
        }

        private Task<HttpResponseMessage> ProcessRequest(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.SetProperty(ScriptConstants.AzureFunctionsHttpFunctionKey, _functionDescriptor);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
#endif