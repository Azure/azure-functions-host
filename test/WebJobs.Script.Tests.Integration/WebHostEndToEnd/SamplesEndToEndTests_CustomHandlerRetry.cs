// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
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
        public async Task HttpTrigger_CustomHandlerRetry_Get_Succeeds()
        {
            await InvokeHttpTrigger("HttpTrigger");
        }

        private async Task InvokeHttpTrigger(string functionName)
        {
            string uri = $"api/{functionName}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string responseContent = await response.Content.ReadAsStringAsync();
            Assert.Equal(responseContent, "Retry Count:2 Max Retry Count:2");
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
