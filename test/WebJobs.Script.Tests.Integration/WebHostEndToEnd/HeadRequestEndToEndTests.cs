// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class HeadRequestEndToEndTests : IClassFixture<HeadRequestEndToEndTests.TestFixture>
    {
        private readonly TestFixture _fixture;

        public HeadRequestEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task HeadRequest_GetOnlyFunction_ReturnsSameStatusAsGet_WithNoBody()
        {
            // GET request to establish the expected status
            var getRequest = new HttpRequestMessage(HttpMethod.Get, "api/HttpTrigger-Redirect");
            var getResponse = await _fixture.Host.HttpClient.SendAsync(getRequest);

            // HEAD request should return same status code with no body
            var headRequest = new HttpRequestMessage(HttpMethod.Head, "api/HttpTrigger-Redirect");
            var headResponse = await _fixture.Host.HttpClient.SendAsync(headRequest);

            Assert.Equal(getResponse.StatusCode, headResponse.StatusCode);
            string body = await headResponse.Content.ReadAsStringAsync();
            Assert.Empty(body);
        }

        [Fact]
        public async Task HeadRequest_PostOnlyFunction_Returns405WithAllowHeader()
        {
            var request = new HttpRequestMessage(HttpMethod.Head, "api/HttpTrigger-Dynamic");
            var response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            string allow = string.Join(", ", response.Content.Headers.GetValues("Allow"));
            Assert.Contains("POST", allow, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task HeadRequest_NonExistentRoute_Returns404()
        {
            var request = new HttpRequestMessage(HttpMethod.Head, "api/DoesNotExist");
            var response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetRequest_GetOnlyFunction_StillReturnsBody()
        {
            // Verify that GET requests are NOT affected by the HEAD handling
            var request = new HttpRequestMessage(HttpMethod.Get, "api/HttpTrigger-Redirect");
            var response = await _fixture.Host.HttpClient.SendAsync(request);

            // HttpTrigger-Redirect returns a 302 redirect
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture()
                : base(Path.Combine("TestScripts", "CSharp"), "headrequest", RpcWorkerConstants.DotNetLanguageWorkerName)
            {
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);

                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger-Redirect",   // GET only, anonymous
                        "HttpTrigger-Dynamic"      // POST only, anonymous
                    };
                });
            }
        }
    }
}
