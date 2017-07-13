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

            string guid = Guid.NewGuid().ToString();

            ScenarioInput input = new ScenarioInput
            {
                Scenario = "appInsights",
                Container = "scenarios-output",
                Value = guid
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
            logs.Single(p => p.EndsWith(guid));

            Assert.Equal(10, _fixture.TelemetryItems.Count);

            // Pull out the function log and verify; it's timestamp may make the following ordering
            // tough to verify.
            TelemetryPayload telemetryItem = _fixture.TelemetryItems.Single(t => t.Data.BaseData.Message == guid);
            ValidateTrace(telemetryItem, guid, LogCategories.Function);
            _fixture.TelemetryItems.Remove(telemetryItem);

            // The Host lock message is on another thread and may fire out of order.
            // https://github.com/Azure/azure-webjobs-sdk-script/issues/1674
            telemetryItem = _fixture.TelemetryItems.Single(t => t.Data.BaseData.Message.StartsWith("Host lock lease acquired by instance ID"));
            ValidateTrace(telemetryItem, "Host lock lease acquired by instance ID", ScriptConstants.LogCategoryHostGeneral);
            _fixture.TelemetryItems.Remove(telemetryItem);

            // Enqueue by time as the requests may come in slightly out-of-order
            Queue<TelemetryPayload> telemetryQueue = new Queue<TelemetryPayload>();
            _fixture.TelemetryItems.OrderBy(t => t.Time).ToList().ForEach(t => telemetryQueue.Enqueue(t));

            telemetryItem = telemetryQueue.Dequeue();
            ValidateTrace(telemetryItem, "Reading host configuration file", LogCategories.Startup);

            telemetryItem = telemetryQueue.Dequeue();
            ValidateTrace(telemetryItem, "Host configuration file read:", LogCategories.Startup);

            telemetryItem = telemetryQueue.Dequeue();
            ValidateTrace(telemetryItem, "Generating 26 job function(s)", LogCategories.Startup);

            telemetryItem = telemetryQueue.Dequeue();
            ValidateTrace(telemetryItem, "Found the following functions:\r\n", LogCategories.Startup);

            telemetryItem = telemetryQueue.Dequeue();
            ValidateTrace(telemetryItem, "Job host started", LogCategories.Startup);

            // Even though the RequestTelemetry comes last, the timestamp is at the beginning of the invocation
            telemetryItem = telemetryQueue.Dequeue();
            ValidateRequest(telemetryItem);

            telemetryItem = telemetryQueue.Dequeue();
            ValidateTrace(telemetryItem, "Function started (Id=", LogCategories.Executor);

            telemetryItem = telemetryQueue.Dequeue();
            ValidateTrace(telemetryItem, "Function completed (Success, Id=", LogCategories.Executor);
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

            Assert.NotNull(telemetryItem.Data.BaseData.Properties["param__blob"]);
            Assert.NotNull(telemetryItem.Data.BaseData.Properties["param__input"]);
            Assert.NotNull(telemetryItem.Data.BaseData.Properties["param___context"]);
            Assert.Equal("Functions.Scenarios", telemetryItem.Data.BaseData.Properties["FullName"].ToString());
            Assert.Equal("This function was programmatically called via the host APIs.", telemetryItem.Data.BaseData.Properties["TriggerReason"].ToString());

            ValidateSdkVersion(telemetryItem);
        }

        private static void ValidateSdkVersion(TelemetryPayload telemetryItem)
        {
            Assert.StartsWith("azurefunctions: ", telemetryItem.Tags["ai.internal.sdkVersion"]);
        }
    }
}
