// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_CustomHandlerRetry : IClassFixture<SamplesEndToEndTests_CustomHandlerRetry.TestFixture>
    {
        private TestFixture _fixture;

        public SamplesEndToEndTests_CustomHandlerRetry(TestFixture fixture)
        {
            _fixture = fixture;
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
            string responseContent = await response.Content.ReadAsStringAsync();
            string expectedContent = "Retry Count:2 Max Retry Count:2";

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Assert.True(false, BuildFailureMessage(functionName, uri, response, responseContent, expectedContent));
            }

            if (!string.Equals(responseContent, expectedContent, StringComparison.Ordinal))
            {
                Assert.True(false, BuildFailureMessage(functionName, uri, response, responseContent, expectedContent));
            }
        }

        private string BuildFailureMessage(string functionName, string uri, HttpResponseMessage response, string responseContent, string expectedContent)
        {
            var message = new StringBuilder();
            message.AppendLine($"CustomHandlerRetry invocation failed for '{functionName}'.");
            message.AppendLine($"Request URI: {uri}");
            message.AppendLine($"Expected status: {HttpStatusCode.OK}");
            message.AppendLine($"Actual status: {response.StatusCode}");
            message.AppendLine($"Expected body: {expectedContent}");
            message.AppendLine($"Actual body: {responseContent}");
            message.AppendLine($"Root script path: {_fixture.RootScriptPath}");
            message.AppendLine($"Host log path: {_fixture.Host.LogPath}");
            message.AppendLine();
            message.AppendLine("Host logs:");
            message.AppendLine(_fixture.Host.GetLog());

            return message.ToString();
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "sample", "CustomHandlerRetry"), "samples", RpcWorkerConstants.PowerShellLanguageWorkerName)
            {
            }

            protected override Task CreateTestStorageEntities()
            {
                // no need
                return Task.CompletedTask;
            }
        }
    }
}
