// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ProxyEndToEndTests : IClassFixture<ProxyEndToEndTests.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public ProxyEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact]
        public async Task Proxy_Invoke_Succeeds()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"/mymockhttp");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(response.Headers.GetValues("myversion").ToArray()[0], "123");
        }

        [Theory]
        [InlineData("test.txt")]
        [InlineData("test.asp")]
        [InlineData("test.aspx")]
        [InlineData("test.svc")]
        [InlineData("test.html")]
        [InlineData("test.css")]
        [InlineData("test.js")]
        public async Task File_Extensions_Test(string fileName)
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"/proxyextensions/{fileName}");

            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal("test", content);
        }

        [Fact]
        public async Task RootCheck()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync("/");

            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal("Root", content);
        }

        [Fact]
        public async Task LocalFunctionCall()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"myhttptrigger");

            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal("Pong", content);
        }

        [Fact]
        public async Task LocalFunctionInfiniteRedirectTest()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"api/myloop");

            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("400", response.StatusCode.ToString("D"));
            Assert.True(content.Contains("Infinite loop"));
        }

        [Fact]
        public async Task LocalFunctionCallWithoutProxy()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"api/Ping");

            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal("Pong", content);
        }

        [Fact]
        public async Task LocalFunctionRouteCallWithoutProxy()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"api/myroute/mysubroute");

            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal("Pong", content);
        }

        [Fact]
        public async Task LocalFunctionCallForNonAlphanumericProxyName()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"MyHttpWithNonAlphanumericProxyName");

            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal("Pong", content);
        }

        [Fact]
        public async Task CatchAllApis()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"api/proxy/blahblah");

            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal("Pong", content);
        }

        [Fact]
        public async Task CatchAllWithCustomRoutes()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"proxy/api/myroute/mysubroute");

            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal("Pong", content);
        }

        [Fact]
        public async Task CatchAllWithCustomRoutesWithInvalidVerb()
        {
            HttpResponseMessage response = await _fixture.HttpClient.PutAsync($"proxy/api/myroute/mysubroute", null);

            Assert.Equal("404", response.StatusCode.ToString("D"));
        }

        [Fact]
        public async Task LongQueryString()
        {
            var longRoute = "/?q=test123412341234123412341234123412341234123412341234123412341234123412341234123421341234123423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234213423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234test123412341234123412341234123412341234123412341234123412341234123412341234123421341234123423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234213423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234test123412341234123412341234123412341234123412341234123412341234123412341234123421341234123423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234213423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234test123412341234123412341234123412341234123412341234123412341234123412341234123421341234123423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234213423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234test123412341234123412341234123412341234123412341234123412341234123412341234123421341234123423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234213423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234test123412341234123412341234123412341234123412341234123412341234123412341234123421341234123423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234213423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234test123412341234123412341234123412341234123412341234123412341234123412341234123421341234123423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234213423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234test123412341234123412341234123412341234123412341234123412341234123412341234123421341234123423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234213423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234test123412341234123412341234123412341234123412341234123412341234123412341234123421341234123423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234213423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234";
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync(longRoute);

            string content = await response.Content.ReadAsStringAsync();

            // This is to make sure the querystring is greater than the default asp.net 2048 characters.
            Assert.True(longRoute.Length > 2048);
            Assert.Equal("200", response.StatusCode.ToString("D"));
        }

        [Fact]
        public async Task LongRoute()
        {
            var longRoute = "test123412341234123412341234123412341234123412341234123412341234123412341234123421341234123423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234213423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234";
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync(longRoute);

            string content = await response.Content.ReadAsStringAsync();

            // This is to make sure the url is greater than the default asp.net 260 characters.
            Assert.True(longRoute.Length > 260);
            Assert.Equal("200", response.StatusCode.ToString("D"));
        }

        [Fact]
        public async Task ProxyCallingLocalProxy()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"/pr1/api/Ping");

            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal("Pong", content);
        }

        [Fact]
        public async Task LocalFunctionCallBodyOverride()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"/mylocalhttpoverride");

            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal("201", response.StatusCode.ToString("D"));
            Assert.Equal("test", response.ReasonPhrase);
            Assert.Equal("{\"test\":\"123\"}", content);
        }

        [Fact]
        public async Task ExternalCallBodyOverride()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"/myexternalhttpoverride");

            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal("201", response.StatusCode.ToString("D"));
            Assert.Equal("test", response.ReasonPhrase);
            Assert.Equal("{\"test\":\"123\"}", content);
        }

        public class TestFixture : IDisposable
        {
            private readonly TestServer _testServer;

            public TestFixture()
            {
                HostSettings = new WebHostSettings
                {
                    IsSelfHost = true,
                    ScriptPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\TestScripts\Proxies"),
                    LogPath = Path.Combine(Path.GetTempPath(), @"ProxyTests\Logs"),
                    SecretsPath = Path.Combine(Path.GetTempPath(), @"ProxyTests\Secrets"),
                    IsAuthDisabled = true
                };

               _testServer = new TestServer(
               AspNetCore.WebHost.CreateDefaultBuilder()
               .UseStartup<Startup>()
               .ConfigureServices(services =>
               {
                   services.Replace(new ServiceDescriptor(typeof(WebHostSettings), HostSettings));
                   services.Replace(new ServiceDescriptor(typeof(ISecretManager), new TestSecretManager()));
               }));

                var scriptConfig = _testServer.Host.Services.GetService<WebHostResolver>().GetScriptHostConfiguration(HostSettings);

                HttpClient = _testServer.CreateClient();
                HttpClient.BaseAddress = new Uri("https://localhost/");

                TestHelpers.WaitForWebHost(HttpClient);
            }

            public WebHostSettings HostSettings { get; private set; }

            public HttpClient HttpClient { get; set; }

            public HttpServer HttpServer { get; set; }

            public void Dispose()
            {
                _testServer?.Dispose();
                HttpServer?.Dispose();
                HttpClient?.Dispose();

                TestHelpers.ClearHostLogs();
            }
        }
    }
}
