// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.DataContracts;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public class ApplicationInsightsIgnoredNodeEndToEndTests : IClassFixture<ApplicationInsightsIgnoredNodeEndToEndTests.TestFixture>
    {
        private ApplicationInsightsTestFixture _fixture;

        public ApplicationInsightsIgnoredNodeEndToEndTests(TestFixture fixture)
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

            await TestHelpers.Await(() =>
            {
                var loggerLogs = _fixture.TestHost.GetScriptHostLogMessages()
                    .Count(p => p.EventId.Name == "FunctionCompleted");

                var appInsightsLogs = _fixture.Channel.Telemetries
                    .Count(p => p is TraceTelemetry t && t.Message.StartsWith("Executed"));

                // We've now seen all 5 executions in host logs
                return loggerLogs == invocationCount && appInsightsLogs == invocationCount;
            });

            var appInsightsFromWorker = _fixture.Channel.Telemetries
                .Where(p => p is ISupportProperties t && t.Properties["Category"].EndsWith(".User"));

            // if ignoreAppInsightsFromWorker is false, this is 5, as the test function writes out
            // one log per invocation
            Assert.Equal(0, appInsightsFromWorker.Count());

            // However, logs should still have made it to the other loggers
            var loggerFromWorker = _fixture.TestHost.GetScriptHostLogMessages()
                    .Where(p => p.Category.EndsWith(".User"));

            Assert.Equal(5, loggerFromWorker.Count());
        }

        public class TestFixture : ApplicationInsightsTestFixture
        {
            private const string ScriptRoot = @"TestScripts\Node";

            public TestFixture() : base(ScriptRoot, "node", ignoreAppInsightsFromWorker: true)
            {
            }
        }
    }
}