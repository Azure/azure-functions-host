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
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
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
        public async Task CatchAll()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"proxy/blahblah");

            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal("Pong", content);
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

        public class TestFixture : IDisposable
        {
            private readonly ScriptSettingsManager _settingsManager;
            private HttpConfiguration _config;
            private TestTraceWriter _traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            public TestFixture()
            {
                _config = new HttpConfiguration();
                _config.Formatters.Add(new PlaintextMediaTypeFormatter());

                _settingsManager = ScriptSettingsManager.Instance;
                HostSettings = new WebHostSettings
                {
                    IsSelfHost = true,
                    ScriptPath = Path.Combine(Environment.CurrentDirectory, @"..\..\TestScripts\Proxies"),
                    LogPath = Path.Combine(Path.GetTempPath(), @"ProxyTests\Logs"),
                    SecretsPath = Path.Combine(Path.GetTempPath(), @"ProxyTests\Secrets"),
                    TraceWriter = _traceWriter
                };
                WebApiConfig.Register(_config, _settingsManager, HostSettings);

                HttpServer = new HttpServer(_config);
                HttpClient = new HttpClient(HttpServer);
                HttpClient.BaseAddress = new Uri("https://localhost/");

                TestHelpers.WaitForWebHost(HttpClient);
            }

            public WebHostSettings HostSettings { get; private set; }

            public HttpClient HttpClient { get; set; }

            public HttpServer HttpServer { get; set; }

            public void Dispose()
            {
                HttpServer?.Dispose();
                HttpClient?.Dispose();

                TestHelpers.ClearHostLogs();
            }
        }
    }
}
