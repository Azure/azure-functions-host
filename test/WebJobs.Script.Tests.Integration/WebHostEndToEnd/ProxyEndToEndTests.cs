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
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ProxyEndToEndTests : IClassFixture<ProxyEndToEndTests.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;
        private string _hostName;

        public ProxyEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
            _hostName = new HostNameProvider(SystemEnvironment.Instance).Value ?? "localhost";
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

            Assert.Equal(24, metadata.Length);
            var function = metadata.Single(p => p.Name == "PingRoute");
            Assert.Equal($"https://{_hostName}/api/myroute/mysubroute", function.InvokeUrlTemplate.AbsoluteUri);

            function = metadata.Single(p => p.Name == "Ping");
            Assert.Equal($"https://{_hostName}/api/ping", function.InvokeUrlTemplate.AbsoluteUri);

            function = metadata.Single(p => p.Name == "LocalFunctionCall");
            Assert.Equal($"https://{_hostName}/api/myhttptrigger", function.InvokeUrlTemplate.AbsoluteUri);

            function = metadata.Single(p => p.Name == "PingMakeResponse");
            Assert.Equal($"https://{_hostName}/api/pingmakeresponse", function.InvokeUrlTemplate.AbsoluteUri);

            // get functions omitting proxies
            uri = "admin/functions";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, "1234");
            response = await _fixture.HttpClient.SendAsync(request);
            metadata = (await response.Content.ReadAsAsync<IEnumerable<FunctionMetadataResponse>>()).ToArray();
            Assert.False(metadata.Any(p => p.IsProxy));
            Assert.Equal(4, metadata.Length);
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
        public async Task LocalFunctionCall_ModifyResponse()
        {
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, $"pingMakeResponseProxy");
            req.Headers.Add("return_test_header", "1");
            req.Headers.Add("return_201", "1");
            HttpResponseMessage response = await _fixture.HttpClient.SendAsync(req);
            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("201", response.StatusCode.ToString("D"));
            Assert.Equal("test_header_from_function_value", response.Headers.GetValues("test_header_from_function").First());
            Assert.Equal("test_header_from_override_value", response.Headers.GetValues("test_header_from_override").First());
            Assert.Equal(@"Pong", content);
        }

        [Fact]
        public async Task LocalFunctionCall_Redirect()
        {
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, $"pingMakeResponseProxy");
            req.Headers.Add("redirect", "1");
            HttpResponseMessage response = await _fixture.HttpClient.SendAsync(req);
            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal("302", response.StatusCode.ToString("D"));
            Assert.Equal("http://www.redirects-regardless.com/", response.Headers.Location.ToString());
            Assert.Equal(@"Pong", content);
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
            Assert.Equal($"http://localhost/api/myroute/mysubroute?a=1", content);
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

        // sets the shared environment variable to enable proxies
        public static void EnableProxiesOnSystemEnvironment()
        {
            // the common code pattern here uses SystemEnvironment, not IEnvironment - so it can't really be nicely injected
            if (!SystemEnvironment.Instance.IsProxiesEnabled()) // only need to do this once (multiple fixtures might need it independently)
            {
                var oldValue = SystemEnvironment.Instance.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags);
                var newValue = string.IsNullOrWhiteSpace(oldValue) ? ScriptConstants.FeatureFlagEnableProxies : $"{oldValue},{ScriptConstants.FeatureFlagEnableProxies}";
                SystemEnvironment.Instance.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, newValue);
                System.Diagnostics.Debug.Assert(SystemEnvironment.Instance.IsProxiesEnabled(), "proxies should now be enabled");
            }
        }

        public class TestFixture : IDisposable
        {
            private readonly string _testHome;

            public TestFixture()
            {
                EnableProxiesOnSystemEnvironment();
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

                var hostProvider = new HostFunctionMetadataProvider(optionsMonitor, NullLogger<HostFunctionMetadataProvider>.Instance, new TestMetricsLogger(), SystemEnvironment.Instance);
                var provider = new FunctionMetadataProvider(NullLogger<FunctionMetadataProvider>.Instance, null, hostProvider, new OptionsWrapper<FunctionsHostingConfigOptions>(new FunctionsHostingConfigOptions()), SystemEnvironment.Instance);

                TestHost = new TestFunctionHost(HostOptions.ScriptPath, HostOptions.LogPath,
                    configureScriptHostServices: services =>
                    {
                        services.Replace(new ServiceDescriptor(typeof(IOptions<ScriptApplicationHostOptions>), new OptionsWrapper<ScriptApplicationHostOptions>(HostOptions)));
                        services.Replace(new ServiceDescriptor(typeof(ISecretManagerProvider), new TestSecretManagerProvider(new TestSecretManager())));
                        services.Replace(new ServiceDescriptor(typeof(IOptionsMonitor<ScriptApplicationHostOptions>), optionsMonitor));
                        services.Replace(new ServiceDescriptor(typeof(IFunctionMetadataProvider), provider));
                    });

                HttpClient = TestHost.HttpClient;

                TestHelpers.WaitForWebHost(HttpClient);
            }

            public async Task<string> GetFunctionSecretAsync(string functionName)
            {
                var secretManager = TestHost.SecretManagerProvider.Current;
                var secrets = await secretManager.GetFunctionSecretsAsync(functionName);
                return secrets.First().Value;
            }

            public TestFunctionHost TestHost { get; }

            public ScriptApplicationHostOptions HostOptions { get; private set; }

            public HttpClient HttpClient { get; set; }

            public HttpServer HttpServer { get; set; }

            public void Dispose()
            {
                TestHost?.Dispose();
                HttpServer?.Dispose();
                HttpClient?.Dispose();

                TestHelpers.ClearHostLogs();
                FileUtility.DeleteDirectoryAsync(_testHome, recursive: true);
            }
        }
    }
}