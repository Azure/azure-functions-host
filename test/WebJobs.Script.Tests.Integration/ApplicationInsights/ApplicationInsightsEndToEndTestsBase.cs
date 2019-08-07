// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public abstract class ApplicationInsightsEndToEndTestsBase<TTestFixture>
        : IClassFixture<TTestFixture> where TTestFixture : ApplicationInsightsTestFixture, new()
    {
        private ApplicationInsightsTestFixture _fixture;

        public ApplicationInsightsEndToEndTestsBase(ApplicationInsightsTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Validate_Manual()
        {
            string functionName = "Scenarios";
            int invocationCount = 5;

            List<string> functionTraces = new List<string>();

            // We want to invoke this multiple times specifically to make sure Node invocationIds
            // are correctly being set. Invoke them all first, then come back and validate all.
            for (int i = 0; i < invocationCount; i++)
            {
                string functionTrace = $"Function trace: {Guid.NewGuid().ToString()}";
                functionTraces.Add(functionTrace);

                JObject input = new JObject()
                {
                    { "scenario", "appInsights" },
                    { "container", "not-used" },
                    { "value",  functionTrace }
                };

                await _fixture.TestHost.BeginFunctionAsync(functionName, input);
            }

            // Now validate each function invocation.
            foreach (string functionTrace in functionTraces)
            {
                await WaitForFunctionTrace(functionName, functionTrace);

                string invocationId = await ValidateEndToEndTestAsync(functionName, functionTrace, true);

                // TODO: Remove this check when metrics are supported in Node:
                // https://github.com/Azure/azure-functions-host/issues/2189
                if (this is ApplicationInsightsCSharpEndToEndTests)
                {
                    // Do some additional metric validation
                    MetricTelemetry metric = _fixture.Channel.Telemetries
                        .OfType<MetricTelemetry>()
                        .Where(t => GetInvocationId(t) == invocationId)
                        .Single();
                    ValidateMetric(metric, invocationId, functionName);
                }
            }

            // App Insights logs first, so wait until this metric appears
            string metricKey = MetricsEventManager.GetAggregateKey(MetricEventNames.FunctionUserLog, functionName);
            IEnumerable<string> GetMetrics() => _fixture.MetricsLogger.LoggedEvents.Where(p => p == metricKey);

            // TODO: Remove this check when metrics are supported in Node:
            // https://github.com/Azure/azure-functions-host/issues/2189
            int expectedCount = this is ApplicationInsightsCSharpEndToEndTests ? 10 : 5;
            await TestHelpers.Await(() => GetMetrics().Count() == expectedCount,
                timeout: 15000, userMessageCallback: () => string.Join(Environment.NewLine, GetMetrics().Select(p => p.ToString())));
        }

        [Fact]
        public async Task Validate_Http_Success()
        {
            await RunHttpTest("HttpTrigger-Scenarios", "appInsights-Success", HttpStatusCode.OK, true);
        }

        [Fact]
        public async Task Validate_Http_Failure()
        {
            await RunHttpTest("HttpTrigger-Scenarios", "appInsights-Failure", HttpStatusCode.Conflict, true);
        }

        [Fact]
        public async Task Validate_Http_Throw()
        {
            await RunHttpTest("HttpTrigger-Scenarios", "appInsights-Throw", HttpStatusCode.InternalServerError, false);
        }

        private async Task RunHttpTest(string functionName, string scenario, HttpStatusCode expectedStatusCode, bool functionSuccess)
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri($"https://localhost/api/{functionName}"),
                Method = HttpMethod.Post,
            };

            string functionTrace = $"Function trace: {Guid.NewGuid().ToString()}";

            JObject input = new JObject()
            {
                { "scenario", scenario },
                { "container", "not-used" },
                { "value",  functionTrace },
            };
            request.Content = new StringContent(input.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Content.Headers.ContentLength = input.ToString().Length;

            HttpResponseMessage response = await _fixture.HttpClient.SendAsync(request);
            Assert.Equal(expectedStatusCode, response.StatusCode);

            string invocationId = await ValidateEndToEndTestAsync(functionName, functionTrace, functionSuccess);

            // Perform some additional validation on HTTP properties
            RequestTelemetry requestTelemetry = _fixture.Channel.Telemetries
                .OfType<RequestTelemetry>()
                .Where(t => GetInvocationId(t) == invocationId)
                .Single();

            Assert.Equal(((int)expectedStatusCode).ToString(), requestTelemetry.ResponseCode);
            Assert.Equal("POST", requestTelemetry.Properties["HttpMethod"]);
            Assert.Equal(request.RequestUri, requestTelemetry.Url);
            Assert.Equal(functionSuccess, requestTelemetry.Success);
        }

        private async Task<string> ValidateEndToEndTestAsync(string functionName, string functionTrace, bool functionSuccess)
        {
            // Look for the trace that matches the GUID we passed in. That's how we'll find the
            // function's invocation id.
            TraceTelemetry trace = _fixture.Channel.Telemetries
                    .OfType<TraceTelemetry>()
                    .Where(t => t.Message.Contains(functionTrace))
                    .Single();

            // functions need to log JSON that contains the invocationId and trace
            JObject logPayload = JObject.Parse(trace.Message);
            string logInvocationId = logPayload["invocationId"].ToString();

            string invocationId = trace.Properties[LogConstants.InvocationIdKey];

            // make sure they match
            Assert.Equal(logInvocationId, invocationId);

            // Find the Info traces.
            TraceTelemetry[] traces = _fixture.Channel.Telemetries
                    .OfType<TraceTelemetry>()
                    .Where(t => GetInvocationId(t) == invocationId)
                    .Where(t => !t.Message.StartsWith("Exception")) // we'll verify the exception message separately
                    .Where(t => t.Properties[LogConstants.CategoryNameKey] == LogCategories.CreateFunctionCategory(functionName))
                    .OrderBy(t => t.Message)
                    .ToArray();

            string expectedMessage = functionSuccess ? $"Executed 'Functions.{functionName}' (Succeeded, Id=" : $"Executed 'Functions.{functionName}' (Failed, Id=";
            SeverityLevel expectedLevel = functionSuccess ? SeverityLevel.Information : SeverityLevel.Error;

            ValidateTrace(traces[0], expectedMessage + invocationId, LogCategories.CreateFunctionCategory(functionName), functionName, invocationId, expectedLevel);
            ValidateTrace(traces[1], $"Executing 'Functions.{functionName}' (Reason='This function was programmatically called via the host APIs.', Id=" + invocationId, LogCategories.CreateFunctionCategory(functionName), functionName, invocationId);

            if (!functionSuccess)
            {
                // We see two exceptions: 
                // - 1 with the RequestTelemetry
                // - 1 with the TraceTelemetry that "Function Executed (Failure...")
                ExceptionTelemetry[] exceptions = _fixture.Channel.Telemetries
                    .OfType<ExceptionTelemetry>()
                    .Where(t => GetInvocationId(t) == invocationId)
                    .OrderBy(t => t.Properties[LogConstants.CategoryNameKey])
                    .ToArray();

                Assert.Equal(2, exceptions.Length);

                ValidateException(exceptions[0], invocationId, functionName, LogCategories.CreateFunctionCategory(functionName));
                ValidateException(exceptions[1], invocationId, functionName, LogCategories.Results);
            }

            // The Request is written separately by App Insights so we need to wait for it
            RequestTelemetry requestTelemetry = null;
            await TestHelpers.Await(() =>
            {
                requestTelemetry = _fixture.Channel.Telemetries
                   .OfType<RequestTelemetry>()
                   .Where(t => GetInvocationId(t) == invocationId)
                   .SingleOrDefault();

                return requestTelemetry != null;
            });

            ValidateRequest(requestTelemetry, invocationId, functionName, "req", functionSuccess);

            // Make sure all the operationIds match
            var operationId = requestTelemetry.Context.Operation.Id;
            Assert.Equal(operationId, traces[0].Context.Operation.Id);
            Assert.Equal(operationId, traces[1].Context.Operation.Id);

            return invocationId;
        }

        private async Task WaitForFunctionTrace(string functionName, string functionTrace)
        {
            // watch for the specific user log, then make sure the request telemetry has flushed, which
            // indicates all logging is done for this function invocation
            await TestHelpers.Await(() =>
            {
                bool done = false;
                TraceTelemetry logTrace = _fixture.Channel.Telemetries.OfType<TraceTelemetry>().SingleOrDefault(p => p.Message.Contains(functionTrace) && p.Properties[LogConstants.CategoryNameKey] == LogCategories.CreateFunctionUserCategory(functionName));

                if (logTrace != null)
                {
                    string invocationId = logTrace.Properties[LogConstants.InvocationIdKey];
                    RequestTelemetry request = _fixture.Channel.Telemetries.OfType<RequestTelemetry>().SingleOrDefault(p => GetInvocationId(p) == invocationId);
                    done = request != null;
                }

                return done;
            },
            userMessageCallback: _fixture.TestHost.GetLog);
        }

        private void ValidateException(ExceptionTelemetry telemetry, string expectedInvocationId, string expectedOperationName, string expectedCategory)
        {
            Assert.IsType<FunctionInvocationException>(telemetry.Exception);
            ValidateTelemetry(telemetry, expectedInvocationId, expectedOperationName, expectedCategory, SeverityLevel.Error);
        }

        [Fact]
        public async Task Validate_HostLogs()
        {
            // Validate the host startup traces. Order by message string as the requests may come in
            // slightly out-of-order or on different threads
            TraceTelemetry[] traces = null;

            await TestHelpers.Await(() =>
            {
                traces = _fixture.Channel.Telemetries
                    .OfType<TraceTelemetry>()
                    .Where(t => t.Properties[LogConstants.CategoryNameKey].ToString().StartsWith("Host."))
                    .OrderBy(t => t.Message)
                    .ToArray();

                // When these two messages are logged, we know we've completed initialization.
                return traces
                .Where(t => t.Message.Contains("Host lock lease acquired by instance ID") || t.Message.Contains("Job host started"))
                .Count() == 2;
            }, userMessageCallback: () => string.Join(Environment.NewLine, _fixture.Channel.Telemetries.OfType<TraceTelemetry>().Select(t => t.Message)));

            // Excluding Node buffer deprecation warning for now
            // TODO: Remove this once the issue https://github.com/Azure/azure-functions-nodejs-worker/issues/98 is resolved
            // We may have any number of "Host Status" calls as we wait for startup. Let's ignore them.
            traces = traces.Where(t =>
                !t.Message.Contains("[DEP0005]") &&
                !t.Message.StartsWith("Host Status")
            ).ToArray();

            int expectedCount = 12;
            Assert.True(traces.Length == expectedCount, $"Expected {expectedCount} messages, but found {traces.Length}. Actual logs:{Environment.NewLine}{string.Join(Environment.NewLine, traces.Select(t => t.Message))}");

            int idx = 0;
            ValidateTrace(traces[idx++], "2 functions loaded", LogCategories.Startup);
            ValidateTrace(traces[idx++], "A function whitelist has been specified", LogCategories.Startup);
            ValidateTrace(traces[idx++], "Found the following functions:\r\n", LogCategories.Startup);
            ValidateTrace(traces[idx++], "Generating 2 job function(s)", LogCategories.Startup);
            ValidateTrace(traces[idx++], "Host initialization: ConsecutiveErrors=0, StartupCount=1", LogCategories.Startup);
            ValidateTrace(traces[idx++], "Host initialized (", LogCategories.Startup);
            ValidateTrace(traces[idx++], "Host lock lease acquired by instance ID", ScriptConstants.LogCategoryHostGeneral);
            ValidateTrace(traces[idx++], "Host started (", LogCategories.Startup);
            ValidateTrace(traces[idx++], "Initializing Host", LogCategories.Startup);
            ValidateTrace(traces[idx++], "Job host started", LogCategories.Startup);
            ValidateTrace(traces[idx++], "Loading functions metadata", LogCategories.Startup);
            ValidateTrace(traces[idx++], "Starting Host (HostId=", LogCategories.Startup);
        }

        [Fact]
        public void Validate_BeginScope()
        {
            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { "APPINSIGHTS_INSTRUMENTATIONKEY", "some_key" },
                    });
                })
                .ConfigureDefaultTestWebScriptHost(b =>
                {
                    b.Services.AddSingleton<ITelemetryChannel>(_fixture.Channel);
                });

            using (var host = hostBuilder.Build())
            {
                ILoggerFactory loggerFactory = host.Services.GetService<ILoggerFactory>();

                // Create a logger and try out the configured factory. We need to pretend that it is coming from a
                // function, so set the function name and the category appropriately.
                ILogger logger = loggerFactory.CreateLogger(LogCategories.CreateFunctionCategory("Test"));
                string customGuid = Guid.NewGuid().ToString();

                using (logger.BeginScope(new Dictionary<string, object>
                {
                    [ScriptConstants.LogPropertyFunctionNameKey] = "Test",
                    ["parentId"] = customGuid // use a parent id so we can pull all our traces out
                }))
                {
                    // Now log as if from within a function.

                    // Test that both dictionaries and structured logs work as state
                    // and that nesting works as expected.
                    using (logger.BeginScope("{customKey1}", "customValue1"))
                    {
                        logger.LogInformation("1");

                        using (logger.BeginScope(new Dictionary<string, object>
                        {
                            ["customKey2"] = "customValue2"
                        }))
                        {
                            logger.LogInformation("2");
                        }

                        logger.LogInformation("3");
                    }

                    using (logger.BeginScope("should not throw"))
                    {
                        logger.LogInformation("4");
                    }
                }

                TraceTelemetry[] traces = _fixture.Channel.Telemetries.OfType<TraceTelemetry>()
                    .Where(t => t.Properties.TryGetValue("prop__parentId", out string value) && value == customGuid)
                    .OrderBy(t => t.Message).ToArray();
                Assert.Equal(4, traces.Length);

                // Every telemetry will have {originalFormat}, Category, Level, but we validate those elsewhere.
                // We're only interested in the custom properties.
                Assert.Equal("1", traces[0].Message);
                Assert.Equal(6, traces[0].Properties.Count);
                Assert.Equal("customValue1", traces[0].Properties["prop__customKey1"]);

                Assert.Equal("2", traces[1].Message);
                Assert.Equal(7, traces[1].Properties.Count);
                Assert.Equal("customValue1", traces[1].Properties["prop__customKey1"]);
                Assert.Equal("customValue2", traces[1].Properties["prop__customKey2"]);

                Assert.Equal("3", traces[2].Message);
                Assert.Equal(6, traces[2].Properties.Count);
                Assert.Equal("customValue1", traces[2].Properties["prop__customKey1"]);

                Assert.Equal("4", traces[3].Message);
                Assert.Equal(5, traces[3].Properties.Count);
            }
        }

        private static void ValidateMetric(MetricTelemetry telemetry, string expectedInvocationId, string expectedOperationName)
        {
            ValidateTelemetry(telemetry, expectedInvocationId, expectedOperationName, LogCategories.CreateFunctionUserCategory(expectedOperationName), SeverityLevel.Information);

            Assert.Equal("TestMetric", telemetry.Name);
            Assert.Equal(1234, telemetry.Sum);
            Assert.Equal(50, telemetry.Count);
            Assert.Equal(10.4, telemetry.Min);
            Assert.Equal(23, telemetry.Max);
            Assert.Equal("100", telemetry.Properties[$"{LogConstants.CustomPropertyPrefix}MyCustomMetricProperty"]);

            ValidateSdkVersion(telemetry, "af_");
        }

        protected static void ValidateTrace(TraceTelemetry telemetry, string expectedMessageContains,
            string expectedCategory, string expectedOperationName = null, string expectedInvocationId = null,
            SeverityLevel expectedLevel = SeverityLevel.Information)
        {
            Assert.Contains(expectedMessageContains, telemetry.Message);
            Assert.Equal(expectedLevel, telemetry.SeverityLevel);

            Assert.Equal(expectedCategory, telemetry.Properties[LogConstants.CategoryNameKey]);

            if (expectedCategory.StartsWith("Function."))
            {
                ValidateTelemetry(telemetry, expectedInvocationId, expectedOperationName, expectedCategory, expectedLevel);
            }
            else
            {
                Assert.Null(telemetry.Context.Operation.Name);
                Assert.Null(telemetry.Context.Operation.Id);
            }

            ValidateSdkVersion(telemetry);
        }

        private static void ValidateTelemetry(ITelemetry telemetry, string expectedInvocationId, string expectedOperationName,
            string expectedCategory, SeverityLevel expectedLevel)
        {
            Assert.Equal(expectedInvocationId, ((ISupportProperties)telemetry).Properties[LogConstants.InvocationIdKey]);
            Assert.Equal(expectedOperationName, telemetry.Context.Operation.Name);

            ISupportProperties properties = (ISupportProperties)telemetry;
            Assert.Equal(expectedCategory, properties.Properties[LogConstants.CategoryNameKey]);
            Assert.Equal(expectedLevel.ToString(), properties.Properties[LogConstants.LogLevelKey]);
        }

        private static void ValidateRequest(RequestTelemetry telemetry, string expectedInvocationId, string expectedOperationName,
            string inputParamName, bool success)
        {
            SeverityLevel expectedLevel = success ? SeverityLevel.Information : SeverityLevel.Error;

            ValidateTelemetry(telemetry, expectedInvocationId, expectedOperationName, LogCategories.Results, expectedLevel);

            Assert.NotNull(telemetry.Id);
            Assert.NotNull(telemetry.Duration);
            Assert.Equal(success, telemetry.Success);

            AssertHasKey(telemetry.Properties, "FullName", $"Functions.{expectedOperationName}");
            AssertHasKey(telemetry.Properties, "TriggerReason", "This function was programmatically called via the host APIs.");

            ValidateSdkVersion(telemetry);
        }

        private static void AssertHasKey(IDictionary<string, string> dict, string keyName, string expectedValue = null)
        {
            if (!dict.TryGetValue(keyName, out string actualValue))
            {
                string msg = $"Missing key '{keyName}'. Keys = " + string.Join(", ", dict.Keys);
                Assert.True(false, msg);
            }
            if (expectedValue != null)
            {
                Assert.Equal(expectedValue, actualValue);
            }
        }

        private static void ValidateSdkVersion(ITelemetry telemetry, string prefix = null)
        {
            Assert.StartsWith($"{prefix}azurefunctions: ", telemetry.Context.GetInternalContext().SdkVersion);
        }

        private static string GetInvocationId(ISupportProperties telemetry)
        {
            telemetry.Properties.TryGetValue(LogConstants.InvocationIdKey, out string id);
            return id;
        }
    }
}