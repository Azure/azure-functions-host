// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public abstract class ApplicationInsightsEndToEndTestsBase<TTestFixture> :
        EndToEndTestsBase<TTestFixture> where TTestFixture : ApplicationInsightsTestFixture, new()
    {
        private ApplicationInsightsTestFixture _fixture;

        public ApplicationInsightsEndToEndTestsBase(TTestFixture fixture) : base(fixture)
        {
            _fixture = fixture;
        }

        protected async Task ApplicationInsights_SucceedsTest()
        {
            string functionName = "Scenarios";
            TestHelpers.ClearFunctionLogs(functionName);

            string functionTrace = $"Function trace: {Guid.NewGuid().ToString()}";

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

            // make sure file logs have the info
            IList<string> logs = null;
            await TestHelpers.Await(() =>
            {
                logs = TestHelpers.GetFunctionLogsAsync(functionName, throwOnNoLogs: false).Result;
                return logs.Count > 0;
            });

            // No need for assert; this will throw if there's not one and only one
            logs.Single(p => p.EndsWith(functionTrace));

            Assert.Equal(12, _fixture.TelemetryItems.Count);

            // Validate the traces. Order by message string as the requests may come in
            // slightly out-of-order or on different threads
            TelemetryPayload[] telemetries = _fixture.TelemetryItems
                .Where(t => t.Data.BaseType == "MessageData")
                .OrderBy(t => t.Data.BaseData.Message)
                .ToArray();

            ValidateTrace(telemetries[0], "Found the following functions:\r\n", LogCategories.Startup);
            ValidateTrace(telemetries[1], "Function completed (Success, Id=", LogCategories.Executor);
            ValidateTrace(telemetries[2], "Function started (Id=", LogCategories.Executor);
            ValidateTrace(telemetries[3], functionTrace, LogCategories.Function);
            ValidateTrace(telemetries[4], "Generating 1 job function(s)", LogCategories.Startup);
            ValidateTrace(telemetries[5], "Host configuration file read:", LogCategories.Startup);
            ValidateTrace(telemetries[6], "Host lock lease acquired by instance ID", ScriptConstants.LogCategoryHostGeneral);
            ValidateTrace(telemetries[7], "Job host started", LogCategories.Startup);
            ValidateTrace(telemetries[8], "Loaded custom extension: BotFrameworkConfiguration from ''", LogCategories.Startup);
            ValidateTrace(telemetries[9], "Loaded custom extension: SendGridConfiguration from ''", LogCategories.Startup);
            ValidateTrace(telemetries[10], "Reading host configuration file", LogCategories.Startup);

            // Finally, validate the request
            TelemetryPayload request = _fixture.TelemetryItems
                .Where(t => t.Data.BaseType == "RequestData")
                .Single();
            ValidateRequest(request);
        }

        private bool IsIndexingError(TelemetryPayload t)
        {
            if (t.Data.BaseType == "ExceptionData")
            {
                return true;
            }
            var message = t.Data.BaseData.Message;
            if (message != null && message.StartsWith("Microsoft.Azure.WebJobs.Host: Error indexing method "))
            {
                return true;
            }
            return false;
        }

        private static void ValidateTrace(TelemetryPayload telemetryItem, string expectedMessageStartsWith, string expectedCategory)
        {
            Assert.Equal("MessageData", telemetryItem.Data.BaseType);

            Assert.StartsWith(expectedMessageStartsWith, telemetryItem.Data.BaseData.Message);
            Assert.Equal("Information", telemetryItem.Data.BaseData.SeverityLevel);

            Assert.Equal(expectedCategory, telemetryItem.Data.BaseData.Properties["Category"]);

            if (expectedCategory == LogCategories.Function || expectedCategory == LogCategories.Executor)
            {
                // These should have associated operation information
                Assert.Equal("Scenarios", telemetryItem.Tags["ai.operation.name"]);
                Assert.NotNull(telemetryItem.Tags["ai.operation.id"]);
            }
            else
            {
                Assert.DoesNotContain("ai.operation.name", telemetryItem.Tags.Keys);
                Assert.DoesNotContain("ai.operation.id", telemetryItem.Tags.Keys);
            }

            ValidateSdkVersion(telemetryItem);
        }

        private static void ValidateRequest(TelemetryPayload telemetryItem)
        {
            Assert.Equal("RequestData", telemetryItem.Data.BaseType);

            Assert.NotNull(telemetryItem.Data.BaseData.Id);
            Assert.Equal("Scenarios", telemetryItem.Data.BaseData.Name);
            Assert.NotNull(telemetryItem.Data.BaseData.Duration);
            Assert.True(telemetryItem.Data.BaseData.Success);

            AssertHasKey(telemetryItem.Data.BaseData.Properties, "param__blob");
            AssertHasKey(telemetryItem.Data.BaseData.Properties, "param__input");
            AssertHasKey(telemetryItem.Data.BaseData.Properties, "param___context");
            AssertHasKey(telemetryItem.Data.BaseData.Properties, "FullName", "Functions.Scenarios");
            AssertHasKey(telemetryItem.Data.BaseData.Properties, "TriggerReason", "This function was programmatically called via the host APIs.");

            ValidateSdkVersion(telemetryItem);
        }

        private static void AssertHasKey(IDictionary<string, string> dict, string keyName, string expectedValue = null)
        {
            string actualValue;
            if (!dict.TryGetValue(keyName, out actualValue))
            {
                var msg = $"Missing key '${keyName}'. Keys=" + string.Join(",", dict.Keys);
                Assert.True(false, msg);
            }
            if (expectedValue != null)
            {
                Assert.Equal(expectedValue, actualValue);
            }
        }

        private static void ValidateSdkVersion(TelemetryPayload telemetryItem)
        {
            Assert.StartsWith("azurefunctions: ", telemetryItem.Tags["ai.internal.sdkVersion"]);
        }
    }
}
