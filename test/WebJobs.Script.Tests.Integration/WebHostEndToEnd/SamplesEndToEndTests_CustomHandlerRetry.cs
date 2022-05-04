// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_CustomHandlerRetry : IClassFixture<SamplesEndToEndTests_CustomHandlerRetry.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public SamplesEndToEndTests_CustomHandlerRetry(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact]
        public async Task HttpTrigger_Retry_LogWarning()
        {
            await InvokeHttpTrigger("HttpTrigger");
        }

        private async Task InvokeHttpTrigger(string functionName)
        {
            string uri = $"api/{functionName}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await TestHelpers.Await(() =>
            {
                var scriptLogs = _fixture.Host.GetScriptHostLogMessages();
                return scriptLogs.Where(x => !string.IsNullOrEmpty(x.FormattedMessage) && x.FormattedMessage.Contains("Retries are not supported for function 'Functions.HttpTrigger'.")).Count() == 1;
            }, 10000, 1000);
        }

        [Fact]
        public async Task Timer_RetryFunctionJson_WorksAsExpected()
        {
            await TestHelpers.Await(() =>
            {
                var scriptLogs = _fixture.Host.GetScriptHostLogMessages();
                int attemptsCount = scriptLogs.Where(x => !string.IsNullOrEmpty(x.FormattedMessage) && x.FormattedMessage.Contains("Waiting for `00:00:01` before retrying function execution. Next attempt:")).Count();
                bool isSuccessful = scriptLogs.Where(x => !string.IsNullOrEmpty(x.FormattedMessage) && x.FormattedMessage.Contains("Executed 'Functions.TimerTrigger' (Succeeded")).Count() == 1;
                return attemptsCount == 4 && isSuccessful;
            }, 10000, 1000);
        }

        public class TestFixture : EndToEndTestFixture
        {
            static TestFixture()
            {
            }

            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "sample", "CustomHandlerRetry"), "samples", RpcWorkerConstants.PowerShellLanguageWorkerName)
            {
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);
            }
        }
    }
}
