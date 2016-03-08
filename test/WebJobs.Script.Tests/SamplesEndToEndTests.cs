// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using WebJobs.Script.WebHost;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class SamplesEndToEndTests : IClassFixture<SamplesEndToEndTests.TestFixture>
    {
        private TestFixture _fixture;

        public SamplesEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Home_Get_Succeeds()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty);

            HttpResponseMessage response = await this._fixture.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task HttpTrigger_Get_Succeeds()
        {
            string uri = "api/httptrigger?code=hyexydhln844f2mb7hgsup2yf8dowlb0885mbiq1&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            HttpResponseMessage response = await this._fixture.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("Hello Mathew", body);
        }

        [Fact]
        public async Task GenericWebHook_Post_Succeeds()
        {
            string uri = "api/webhook-generic?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'a': 'Foobar' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("WebHook processed successfully! Foobar", body);
        }

        [Fact]
        public async Task GenericWebHook_Post_AdminKey_Succeeds()
        {
            // Verify that sending the admin key bypasses WebHook auth
            string uri = "api/webhook-generic?code=t8laajal0a1ajkgzoqlfv5gxr4ebhqozebw4qzdy";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'a': 'Foobar' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("WebHook processed successfully! Foobar", body);
        }

        public class TestFixture
        {
            public TestFixture()
            {
                HttpConfiguration config = new HttpConfiguration();

                WebHostSettings settings = new WebHostSettings
                {
                    IsSelfHost = true,
                    ScriptPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\sample"),
                    LogPath = Path.Combine(Path.GetTempPath(), @"Functions"),
                    SecretsPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\src\WebJobs.Script.WebHost\App_Data\Secrets")
                };
                WebApiConfig.Register(config, settings);

                HttpServer server = new HttpServer(config);
                this.Client = new HttpClient(server);
                this.Client.BaseAddress = new Uri("https://localhost/");
            }

            public HttpClient Client { get; set; }
        }
    }
}
