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
            ScriptHost host = _fixture.GetScriptHost();

            // We want to invoke this multiple times specifically to make sure Node invocationIds
            // are correctly being set. Invoke them all first, then come back and validate all.
            for (int i = 0; i < invocationCount; i++)
            {
                string functionTrace = $"Function trace: {Guid.NewGuid().ToString()}";
                functionTraces.Add(functionTrace);

                Dictionary<string, object> input = new Dictionary<string, object>
                {
                    ["input"] = new JObject()
                        {
                            { "scenario", "appInsights" },
                            { "container", "not-used" },
                            { "value",  functionTrace }
                        }.ToString()
                };

                await host.CallAsync(functionName, input);
            }

            // Now validate each function invocation.
            foreach (string functionTrace in functionTraces)
            {
                await WaitForFunctionTrace(functionName, functionTrace);

                string invocationId = ValidateEndToEndTest(functionName, functionTrace, true);

                // TODO: Remove this check when metrics are supported in Node:
                // https://github.com/Azure/azure-functions-host/issues/2189
                if (this is ApplicationInsightsCSharpEndToEndTests)
                {
                    // Do some additional metric validation
                    MetricTelemetry metric = _fixture.Channel.Telemetries
                        .OfType<MetricTelemetry>()
                        .Where(t => t.Context.Operation.Id == invocationId)
                        .Single();
                    ValidateMetric(metric, invocationId, functionName);
                }
            }
        }

        [Fact(Skip = "HTTP logging not currently supported")]
        public async Task Validate_Http_Success()
        {
            await RunHttpTest("HttpTrigger-Scenarios", "appInsights-Success", HttpStatusCode.OK, true);
        }

        [Fact(Skip = "HTTP logging not currently supported")]
        public async Task Validate_Http_Failure()
        {
            await RunHttpTest("HttpTrigger-Scenarios", "appInsights-Failure", HttpStatusCode.Conflict, true);
        }

        [Fact(Skip = "HTTP logging not currently supported")]
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

            string invocationId = ValidateEndToEndTest(functionName, functionTrace, functionSuccess);

            // Perform some additional validation on HTTP properties
            RequestTelemetry requestTelemetry = _fixture.Channel.Telemetries
                .OfType<RequestTelemetry>()
                .Where(t => t.Context.Operation.Id == invocationId)
                .Single();

            Assert.Equal(((int)expectedStatusCode).ToString(), requestTelemetry.ResponseCode);
            Assert.Equal("POST", requestTelemetry.Properties["HttpMethod"]);
            Assert.Equal(request.RequestUri, requestTelemetry.Url);
            Assert.Equal(functionSuccess, requestTelemetry.Success);
        }

        private string ValidateEndToEndTest(string functionName, string functionTrace, bool functionSuccess)
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

            string invocationId = trace.Context.Operation.Id;

            // make sure they match
            Assert.Equal(logInvocationId, invocationId);

            // Find the Info traces.
            TraceTelemetry[] traces = _fixture.Channel.Telemetries
                    .OfType<TraceTelemetry>()
                    .Where(t => t.Context.Operation.Id == invocationId)
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
                TraceTelemetry errorTrace = _fixture.Channel.Telemetries
                    .OfType<TraceTelemetry>()
                    .Where(t => t.Context.Operation.Id == invocationId)
                    .Where(t => t.Message.Contains("Exception while"))
                    .Single();

                ValidateTrace(errorTrace, $"Exception while executing function: Functions.{functionName}.", LogCategories.CreateFunctionCategory(functionName), functionName, invocationId, SeverityLevel.Error);

                ExceptionTelemetry exception = _fixture.Channel.Telemetries
                    .OfType<ExceptionTelemetry>()
                    .Single(t => t.Context.Operation.Id == invocationId);

                ValidateException(exception, invocationId, functionName, LogCategories.Results);
            }

            RequestTelemetry requestTelemetry = _fixture.Channel.Telemetries
                .OfType<RequestTelemetry>()
                .Where(t => t.Context.Operation.Id == invocationId)
                .Single();

            ValidateRequest(requestTelemetry, invocationId, functionName, "req", functionSuccess);

            return invocationId;
        }

        private async Task WaitForFunctionTrace(string functionName, string functionTrace)
        {
            // make sure the telemetry has flushed
            IEnumerable<string> logs = null;
            await TestHelpers.Await(() =>
            {
                logs = _fixture.Channel.Telemetries.OfType<TraceTelemetry>().Select(p => p.Message);
                return logs.Count(p => p.Contains(functionTrace)) == 1;
            }, pollingInterval: 100);
        }

        private void ValidateException(ExceptionTelemetry telemetry, string expectedOperationId, string expectedOperationName, string expectedCategory)
        {
            Assert.IsType<FunctionInvocationException>(telemetry.Exception);
            ValidateTelemetry(telemetry, expectedOperationId, expectedOperationName, expectedCategory, SeverityLevel.Error);
        }

        [Fact]
        public async Task Validate_HostLogs()
        {
            // Validate the host startup traces. Order by message string as the requests may come in
            // slightly out-of-order or on different threads
            TraceTelemetry[] traces = null;

            int expectedCount = 12;

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
            });

            Assert.True(traces.Length == expectedCount, $"Expected {expectedCount} messages, but found {traces.Length}. Actual logs:{Environment.NewLine}{string.Join(Environment.NewLine, traces.Select(t => t.Message))}");

            ValidateTrace(traces[0], "A function whitelist has been specified", LogCategories.Startup);
            ValidateTrace(traces[1], "Found the following functions:\r\n", LogCategories.Startup);
            ValidateTrace(traces[2], "Generating 2 job function(s)", LogCategories.Startup);
            ValidateTrace(traces[3], "Host configuration file read:", LogCategories.Startup);
            ValidateTrace(traces[4], "Host id explicitly set", LogCategories.Startup, expectedLevel: SeverityLevel.Warning);
            ValidateTrace(traces[5], "Host initialized (", LogCategories.Startup);
            ValidateTrace(traces[6], "Host lock lease acquired by instance ID", ScriptConstants.LogCategoryHostGeneral);
            ValidateTrace(traces[7], "Host started (", LogCategories.Startup);
            ValidateTrace(traces[8], "Job host started", LogCategories.Startup);
            ValidateTrace(traces[9], "Loading all the worker providers from the default workers directory", LogCategories.Startup);
            ValidateTrace(traces[10], "Reading host configuration file", LogCategories.Startup);
            ValidateTrace(traces[11], "Starting Host (HostId=function-tests-", ScriptConstants.LogCategoryHostGeneral);
        }

        private static void ValidateMetric(MetricTelemetry telemetry, string expectedOperationId, string expectedOperationName)
        {
            ValidateTelemetry(telemetry, expectedOperationId, expectedOperationName, LogCategories.CreateFunctionUserCategory(expectedOperationName), SeverityLevel.Information);

            Assert.Equal("TestMetric", telemetry.Name);
            Assert.Equal(1234, telemetry.Sum);
            Assert.Equal(50, telemetry.Count);
            Assert.Equal(10.4, telemetry.Min);
            Assert.Equal(23, telemetry.Max);
            Assert.Equal("100", telemetry.Properties[$"{LogConstants.CustomPropertyPrefix}MyCustomMetricProperty"]);

            ValidateSdkVersion(telemetry);
        }

        protected static void ValidateTrace(TraceTelemetry telemetry, string expectedMessageContains,
            string expectedCategory, string expectedOperationName = null, string expectedOperationId = null,
            SeverityLevel expectedLevel = SeverityLevel.Information)
        {
            Assert.Contains(expectedMessageContains, telemetry.Message);
            Assert.Equal(expectedLevel, telemetry.SeverityLevel);

            Assert.Equal(expectedCategory, telemetry.Properties[LogConstants.CategoryNameKey]);

            if (expectedCategory.StartsWith("Function."))
            {
                ValidateTelemetry(telemetry, expectedOperationId, expectedOperationName, expectedCategory, expectedLevel);
            }
            else
            {
                Assert.Null(telemetry.Context.Operation.Name);
                Assert.Null(telemetry.Context.Operation.Id);
            }

            ValidateSdkVersion(telemetry);
        }

        private static void ValidateTelemetry(ITelemetry telemetry, string expectedOperationId, string expectedOperationName,
            string expectedCategory, SeverityLevel expectedLevel)
        {
            Assert.Equal(expectedOperationId, telemetry.Context.Operation.Id);
            Assert.Equal(expectedOperationName, telemetry.Context.Operation.Name);

            var properties = (ISupportProperties)telemetry;
            Assert.Equal(expectedCategory, properties.Properties[LogConstants.CategoryNameKey]);
            Assert.Equal(expectedLevel.ToString(), properties.Properties[LogConstants.LogLevelKey]);
        }

        private static void ValidateRequest(RequestTelemetry telemetry, string expectedOperationId, string expectedOperationName,
            string inputParamName, bool success)
        {
            SeverityLevel expectedLevel = success ? SeverityLevel.Information : SeverityLevel.Error;

            ValidateTelemetry(telemetry, expectedOperationId, expectedOperationName, LogCategories.Results, expectedLevel);

            Assert.Equal(telemetry.Id, expectedOperationId);
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
                var msg = $"Missing key '{keyName}'. Keys = " + string.Join(", ", dict.Keys);
                Assert.True(false, msg);
            }
            if (expectedValue != null)
            {
                Assert.Equal(expectedValue, actualValue);
            }
        }

        private static void ValidateSdkVersion(ITelemetry telemetry)
        {
            Assert.StartsWith("azurefunctions: ", telemetry.Context.GetInternalContext().SdkVersion);
        }
    }
}