// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_Retry : IClassFixture<SamplesEndToEndTests_Node_Retry.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public SamplesEndToEndTests_Node_Retry(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact]
        public async Task Timer_RetryFunctionJson_WorksAsExpected()
        {
            await TestHelpers.Await(() =>
            {
                var scriptLogs = _fixture.Host.GetScriptHostLogMessages();
                int attemptsCount = scriptLogs.Where(x => !string.IsNullOrEmpty(x.FormattedMessage) && x.FormattedMessage.Contains("Waiting for `00:00:01` before retrying function execution. Next attempt:")).Count();
                bool isSuccessful = scriptLogs.Where(x => !string.IsNullOrEmpty(x.FormattedMessage) && x.FormattedMessage.Contains("Executed 'Functions.Timer-RetryFunctionJson' (Succeeded")).Count() == 1;
                return attemptsCount == 4 && isSuccessful;
            }, 10000, 1000);
        }

        [Fact]
        public async Task HttpTrigger_Retry_LogWarning()
        {
            var response = await SamplesTestHelpers.InvokeHttpTrigger(_fixture, "HttpTrigger-RetryWarning");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await TestHelpers.Await(() =>
            {
                var scriptLogs = _fixture.Host.GetScriptHostLogMessages();
                return scriptLogs.Where(x => !string.IsNullOrEmpty(x.FormattedMessage) && x.FormattedMessage.Contains("Retries are not supported for function 'Functions.HttpTrigger-RetryWarning'.")).Count() == 1;
            }, 10000, 1000);
        }

        public class TestFixture : EndToEndTestFixture
        {
            static TestFixture()
            {
            }

            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "NodeRetry"), "samples", RpcWorkerConstants.NodeLanguageWorkerName)
            {
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);
            }
        }
    }
}