// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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
        public async Task ListFunctions_Proxies_Succeeds()
        {
            // get functions including proxies
            string uri = "admin/functions?includeProxies=true";
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, "1234");
            var response = await _fixture.HttpClient.SendAsync(request);
            var metadata = (await response.Content.ReadAsAsync<IEnumerable<FunctionMetadataResponse>>()).ToArray();

            Assert.Equal(22, metadata.Length);
            var function = metadata.Single(p => p.Name == "PingRoute");
            Assert.Equal("https://localhost/api/myroute/mysubroute", function.InvokeUrlTemplate.AbsoluteUri);

            function = metadata.Single(p => p.Name == "Ping");
            Assert.Equal("https://localhost/api/ping", function.InvokeUrlTemplate.AbsoluteUri);

            function = metadata.Single(p => p.Name == "LocalFunctionCall");
            Assert.Equal("https://localhost/api/myhttptrigger", function.InvokeUrlTemplate.AbsoluteUri);

            // get functions omitting proxies
            uri = "admin/functions";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, "1234");
            response = await _fixture.HttpClient.SendAsync(request);
            metadata = (await response.Content.ReadAsAsync<IEnumerable<FunctionMetadataResponse>>()).ToArray();
            Assert.False(metadata.Any(p => p.IsProxy));
            Assert.Equal(3, metadata.Length);
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
        public async Task LocalFunctionCallWithAuth()
        {
            string functionKey = await _fixture.GetFunctionSecretAsync("PingAuth");

            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"myhttptriggerauth?code={functionKey}");

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
        public async Task ColdStartRequest()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "api/proxy/blahblah");
            request.Headers.Add("X-MS-COLDSTART", "1");
            HttpResponseMessage response = await _fixture.HttpClient.SendAsync(request);

            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal("Pong", content);
        }

        [Fact]
        //backend set as constant - no trailing slash should be added
        public async Task TrailingSlashRemoved()
        {
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, $"staticBackendUrlTest/blahblah/");
            req.Headers.Add("return_incoming_url", "1");
            HttpResponseMessage response = await _fixture.HttpClient.SendAsync(req);
            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal(@"http://localhost/api/myroute/mysubroute?a=1", content);
        }

        [Fact]
        //backend ended with simple param - no trailing slash should be added
        public async Task TrailingSlashRemoved2()
        {
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, $"simpleParamBackendUrlTest/myroute/mysubroute/");
            req.Headers.Add("return_incoming_url", "1");
            HttpResponseMessage response = await _fixture.HttpClient.SendAsync(req);
            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal(@"http://localhost/api/myroute/mysubroute?a=1", content);
        }

        [Fact]
        //backend path ended with wildcard param - slash should be kept
        public async Task TrailingSlashKept()
        {
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, $"wildcardBackendUrlTest/myroute/mysubroute/");
            req.Headers.Add("return_incoming_url", "1");
            HttpResponseMessage response = await _fixture.HttpClient.SendAsync(req);
            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal(@"http://localhost/api/myroute/mysubroute/?a=1", content);
        }

        [Fact]
        //backend path ended with wildcard param - slash should be kept
        public async Task TrailingSlashKept2()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"wildcardBackendUrlTest/myroute/mysubroute");
            req.Headers.Add("return_incoming_url", "1");
            var response = await _fixture.HttpClient.SendAsync(req);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal("200", response.StatusCode.ToString("D"));
            Assert.Equal(@"http://localhost/api/myroute/mysubroute?a=1", content);
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
            Assert.Equal("{\"test\":\"{}{123}\"}", content);
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

        [Fact]
        //"HEAD" request to proxy. backend returns 304 with no body but content-type shouldn't be null
        public async Task EmptyHeadReturnsContentType()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, $"contentTypePresenceTest");
            request.Headers.Add("return_empty_body", "1");
            HttpResponseMessage response = await _fixture.HttpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(string.IsNullOrEmpty(body));
            Assert.Equal(response.StatusCode, HttpStatusCode.NotModified);
            Assert.Equal(response.Content.Headers.GetValues("Content-Type").ToArray()[0], "fake/custom");
            Assert.True(response.Headers.Contains("Test"));
        }

        [Fact]
        //"GET" request to proxy. backend returns 304 with no body so content-type should be null
        public async Task EmptyGetDoesntReturnsContentType()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"contentTypePresenceTest");
            request.Headers.Add("return_empty_body", "1");
            HttpResponseMessage response = await _fixture.HttpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(string.IsNullOrEmpty(body));
            Assert.Equal(response.StatusCode, HttpStatusCode.NotModified);
            Assert.False(response.Content.Headers.Contains("Content-Type"));
            Assert.True(response.Headers.Contains("Test"));
        }


        public class TestFixture : IDisposable
        {
            private readonly TestServer _testServer;
            private readonly string _testHome;

            public TestFixture()
            {
                // copy test files to temp directory, since accessing the metadata APIs will result
                // in file creations (for test data files)
                var scriptSource = Path.Combine(Environment.CurrentDirectory, @"..\..\..\TestScripts\Proxies");
                _testHome = Path.Combine(Path.GetTempPath(), @"ProxyTests");
                var scriptRoot = Path.Combine(_testHome, @"site\wwwroot");
                FileUtility.CopyDirectory(scriptSource, scriptRoot);

                HostOptions = new ScriptApplicationHostOptions
                {
                    IsSelfHost = true,
                    ScriptPath = scriptRoot,
                    LogPath = Path.Combine(_testHome, @"LogFiles\Application\Functions"),
                    SecretsPath = Path.Combine(_testHome, @"data\Functions\Secrets"),
                    TestDataPath = Path.Combine(_testHome, @"data\Functions\SampleData")
                };

                FileUtility.EnsureDirectoryExists(HostOptions.TestDataPath);

                var optionsMonitor = TestHelpers.CreateOptionsMonitor(HostOptions);

                var workerOptions = new LanguageWorkerOptions
                {
                    WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
                };

                var provider = new FunctionMetadataProvider(optionsMonitor, new OptionsWrapper<LanguageWorkerOptions>(workerOptions), NullLogger<FunctionMetadataProvider>.Instance, new TestMetricsLogger());

                var builder = AspNetCore.WebHost.CreateDefaultBuilder()
                   .UseStartup<Startup>()
                   .ConfigureServices(services =>
                   {
                       services.Replace(new ServiceDescriptor(typeof(IOptions<ScriptApplicationHostOptions>), new OptionsWrapper<ScriptApplicationHostOptions>(HostOptions)));
                       services.Replace(new ServiceDescriptor(typeof(ISecretManagerProvider), new TestSecretManagerProvider(new TestSecretManager())));
                       services.Replace(new ServiceDescriptor(typeof(IOptionsMonitor<ScriptApplicationHostOptions>), optionsMonitor));
                       services.Replace(new ServiceDescriptor(typeof(IFunctionMetadataProvider), provider));
                   });

                _testServer = new TestServer(builder);
                HostOptions.RootServiceProvider = _testServer.Host.Services;
                var scriptConfig = _testServer.Host.Services.GetService<IOptions<ScriptJobHostOptions>>().Value;

                HttpClient = _testServer.CreateClient();
                HttpClient.BaseAddress = new Uri("https://localhost/");

                TestHelpers.WaitForWebHost(HttpClient);
            }

            public async Task<string> GetFunctionSecretAsync(string functionName)
            {
                var secretManager = _testServer.Host.Services.GetService<ISecretManagerProvider>().Current;
                var secrets = await secretManager.GetFunctionSecretsAsync(functionName);
                return secrets.First().Value;
            }

            public ScriptApplicationHostOptions HostOptions { get; private set; }

            public HttpClient HttpClient { get; set; }

            public HttpServer HttpServer { get; set; }

            public void Dispose()
            {
                _testServer?.Dispose();
                HttpServer?.Dispose();
                HttpClient?.Dispose();

                TestHelpers.ClearHostLogs();
                FileUtility.DeleteDirectoryAsync(_testHome, recursive: true);
            }
        }
    }
}
