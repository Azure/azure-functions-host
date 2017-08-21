// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public abstract class ApplicationInsightsEndToEndTestsBase<TTestFixture> :
        EndToEndTestsBase<TTestFixture> where TTestFixture : ApplicationInsightsTestFixture, new()
    {
        public ApplicationInsightsEndToEndTestsBase(TTestFixture fixture) : base(fixture)
        {
        }

        protected async Task ApplicationInsights_SucceedsTest()
        {
            string functionName = "Scenarios";
            TestHelpers.ClearFunctionLogs(functionName);

            string functionTrace = $"Function trace: {Guid.NewGuid().ToString()}";
            int invocationCount = 5;

            for (int i = 0; i < invocationCount; i++)
            {
                ScenarioInput input = new ScenarioInput
                {
                    Scenario = "appInsights",
                    Container = "scenarios-output",
                    Value = functionTrace
                };

                Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { "input", JsonConvert.SerializeObject(input) }
                };

                await Fixture.Host.CallAsync(functionName, arguments);
            }

            // make sure file logs have the info
            IList<string> logs = null;
            await TestHelpers.Await(() =>
            {
                logs = TestHelpers.GetFunctionLogsAsync(functionName, throwOnNoLogs: false).Result;
                return logs.Count > 0;
            });

            Assert.Equal(invocationCount, logs.Count(p => p.Contains(functionTrace)));

            // Each invocation produces 5 telemetries. Then there are 9 created by the host.
            Assert.Equal((5 * invocationCount) + 9, Fixture.Channel.Telemetries.Count);

            // Validate the telemetry for a specific function. There should be 5 for each, and these should all
            // include the function name and invocation id. Pull out the function trace first and
            // use the invocation id to find the related items.
            var functionTraces = Fixture.Channel.Telemetries
                .OfType<TraceTelemetry>()
                .Where(t => t.Properties[LogConstants.CategoryNameKey] == LogCategories.Function);

            Assert.Equal(invocationCount, functionTraces.Count());

            foreach (var trace in functionTraces)
            {
                JObject logPayload = JObject.Parse(trace.Message);
                string invocationId = logPayload["InvocationId"].ToString();

                // Find all with this matching operation id. There should be 4.
                ITelemetry[] relatedTelemetry = Fixture.Channel.Telemetries
                    .Where(t => t.Context.Operation.Id == invocationId)
                    .ToArray();

                Assert.Equal(5, relatedTelemetry.Length);

                TraceTelemetry[] traces = relatedTelemetry
                    .OfType<TraceTelemetry>()
                    .OrderBy(t => t.Message)
                    .ToArray();

                ValidateTrace(traces[0], functionTrace, LogCategories.Function, functionName, invocationId);
                ValidateTrace(traces[1], "Function completed (Success, Id=" + invocationId, LogCategories.Executor, functionName, invocationId);
                ValidateTrace(traces[2], "Function started (Id=" + invocationId, LogCategories.Executor, functionName, invocationId);

                MetricTelemetry metric = relatedTelemetry
                    .OfType<MetricTelemetry>()
                    .Single();
                ValidateMetric(metric, functionName);

                RequestTelemetry request = relatedTelemetry
                    .OfType<RequestTelemetry>()
                    .Single();
                ValidateRequest(request, "Scenarios", true);
            }

            // Validate the rest of the traces. Order by message string as the requests may come in
            // slightly out-of-order or on different threads
            TraceTelemetry[] telemetries = Fixture.Channel.Telemetries
                .OfType<TraceTelemetry>()
                .Where(t => t.Context.Operation.Id == null)
                .OrderBy(t => t.Message)
                .ToArray();

            ValidateTrace(telemetries[0], "Found the following functions:\r\n", LogCategories.Startup);
            ValidateTrace(telemetries[1], "Generating 1 job function(s)", LogCategories.Startup);
            ValidateTrace(telemetries[2], "Host configuration file read:", LogCategories.Startup);
            ValidateTrace(telemetries[3], "Host lock lease acquired by instance ID", ScriptConstants.LogCategoryHostGeneral);
            ValidateTrace(telemetries[4], "Job host started", LogCategories.Startup);
            ValidateTrace(telemetries[5], "Loaded custom extension: BotFrameworkConfiguration from ''", LogCategories.Startup);
            ValidateTrace(telemetries[6], "Loaded custom extension: EventGridExtensionConfig from ''", LogCategories.Startup);
            ValidateTrace(telemetries[7], "Loaded custom extension: SendGridConfiguration from ''", LogCategories.Startup);
            ValidateTrace(telemetries[8], "Reading host configuration file", LogCategories.Startup);
        }

        private static void ValidateMetric(MetricTelemetry telemetry, string expectedOperationName)
        {
            Assert.Equal(expectedOperationName, telemetry.Context.Operation.Name);
            Assert.NotNull(telemetry.Context.Operation.Id);
            Assert.Equal(LogCategories.Function, telemetry.Properties[LogConstants.CategoryNameKey]);
            Assert.Equal(LogLevel.Information.ToString(), telemetry.Properties[LogConstants.LogLevelKey]);

            Assert.Equal("TestMetric", telemetry.Name);
            Assert.Equal(1234, telemetry.Sum);
            Assert.Equal(50, telemetry.Count);
            Assert.Equal(10.4, telemetry.Min);
            Assert.Equal(23, telemetry.Max);
            Assert.Equal("100", telemetry.Properties[$"{LogConstants.CustomPropertyPrefix}MyCustomMetricProperty"]);

            ValidateSdkVersion(telemetry);
        }

        protected static void ValidateTrace(TraceTelemetry telemetry, string expectedMessageContains,
            string expectedCategory, string expectedOperationName = null, string expectedOperationId = null)
        {
            Assert.Contains(expectedMessageContains, telemetry.Message);
            Assert.Equal(SeverityLevel.Information, telemetry.SeverityLevel);

            Assert.Equal(expectedCategory, telemetry.Properties[LogConstants.CategoryNameKey]);

            if (expectedCategory == LogCategories.Function || expectedCategory == LogCategories.Executor)
            {
                // These should have associated operation information
                Assert.True(expectedOperationId == telemetry.Context.Operation.Id,
                    $"Unexpected Operation Id. Expected: '{expectedOperationId}'; Actual: '{telemetry.Context.Operation.Id}'; Message: '{telemetry.Message}'");
                Assert.True(expectedOperationName == telemetry.Context.Operation.Name,
                    $"Unexpected Operation Name. Expected: '{expectedOperationName}'; Actual: '{telemetry.Context.Operation.Name}'; Message: '{telemetry.Message}'");
            }
            else
            {
                Assert.Null(telemetry.Context.Operation.Name);
                Assert.Null(telemetry.Context.Operation.Id);
            }

            ValidateSdkVersion(telemetry);
        }

        private static void ValidateRequest(RequestTelemetry telemetry, string operationName, bool success)
        {
            Assert.NotNull(telemetry.Context.Operation.Id);
            Assert.Equal(operationName, telemetry.Context.Operation.Name);
            Assert.NotNull(telemetry.Duration);
            Assert.Equal(success, telemetry.Success);

            AssertHasKey(telemetry.Properties, "param__input");
            AssertHasKey(telemetry.Properties, "FullName", "Functions.Scenarios");
            AssertHasKey(telemetry.Properties, "TriggerReason", "This function was programmatically called via the host APIs.");

            ValidateSdkVersion(telemetry);
        }

        private static void AssertHasKey(IDictionary<string, string> dict, string keyName, string expectedValue = null)
        {
            string actualValue;
            if (!dict.TryGetValue(keyName, out actualValue))
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
            PropertyInfo propInfo = typeof(TelemetryContext).GetProperty("Tags", BindingFlags.NonPublic | BindingFlags.Instance);
            IDictionary<string, string> tags = propInfo.GetValue(telemetry.Context) as IDictionary<string, string>;

            Assert.StartsWith("azurefunctions: ", tags["ai.internal.sdkVersion"]);
        }
    }
}
