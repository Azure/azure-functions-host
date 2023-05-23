// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WarmupFunctionEndToEndTests : IClassFixture<WarmupFunctionEndToEndTests.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public WarmupFunctionEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact]
        public async Task Warmup_Invoke_Succeeds()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"/admin/warmup");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("myversion"), "/admin/warmup cannot be overriden by proxies.");
        }

        [Fact]
        public async Task Normal_Api_Warmup_HttpTrigger_Succeeds()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync("/warmup");

            string content = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode.ToString("D") == "200", "Normal http trigger with 'warmup' route failed to run. ");
            Assert.Equal("Pong", content);
        }

        [Fact]
        public async Task Proxy_Admin_Override_Fails()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync("/admin/123");

            string content = await response.Content.ReadAsStringAsync();
            Assert.False(response.Headers.Contains("myversion"), "/admin/* endpoints cannot be overriden by proxies.");
        }

        [Fact]
        public async Task FunctionRoutes_Admin_Override_Fails()
        {
            HttpResponseMessage response = await _fixture.HttpClient.GetAsync("/admin/host/status");

            string content = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode.ToString("D") == "401", "/admin/* endpoints cannot be overridden by function routes.");
            Assert.True(content == string.Empty, "/admin/* endpoints cannot be overridden by function routes.");
        }


        public class TestFixture : IDisposable
        {
            private readonly TestServer _testServer;
            private readonly string _testHome;

            public TestFixture()
            {
                ProxyEndToEndTests.EnableProxiesOnSystemEnvironment();
                // copy test files to temp directory, since accessing the metadata APIs will result
                // in file creations (for test data files)
                var scriptSource = Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "TestScripts", "WarmupFunction");
                _testHome = Path.Combine(Path.GetTempPath(), @"WarmupFunction");
                var scriptRoot = Path.Combine(_testHome, "site", "wwwroot");
                FileUtility.CopyDirectory(scriptSource, scriptRoot);

                HostOptions = new ScriptApplicationHostOptions
                {
                    IsSelfHost = true,
                    ScriptPath = scriptRoot,
                    LogPath = Path.Combine(_testHome, "LogFiles", "Application", "Functions"),
                    SecretsPath = Path.Combine(_testHome, "data", "Functions", "Secrets"),
                    TestDataPath = Path.Combine(_testHome, "data", "Functions", "SampleData")
                };

                FileUtility.EnsureDirectoryExists(HostOptions.TestDataPath);

                var optionsMonitor = TestHelpers.CreateOptionsMonitor(HostOptions);

                var workerOptions = new LanguageWorkerOptions
                {
                    WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
                };

                var provider = new HostFunctionMetadataProvider(optionsMonitor, NullLogger<HostFunctionMetadataProvider>.Instance, new TestMetricsLogger(), SystemEnvironment.Instance);

                var builder = AspNetCore.WebHost.CreateDefaultBuilder()
                   .UseStartup<Startup>()
                   .ConfigureServices(services =>
                   {
                       services.Replace(new ServiceDescriptor(typeof(IOptions<ScriptApplicationHostOptions>), new OptionsWrapper<ScriptApplicationHostOptions>(HostOptions)));
                       services.Replace(new ServiceDescriptor(typeof(ISecretManagerProvider), new TestSecretManagerProvider(new TestSecretManager())));
                       services.Replace(new ServiceDescriptor(typeof(IOptionsMonitor<ScriptApplicationHostOptions>), optionsMonitor));
                       services.Replace(new ServiceDescriptor(typeof(IFunctionMetadataProvider), provider));

                       services.SkipDependencyValidation();
                   });

                // TODO: https://github.com/Azure/azure-functions-host/issues/4876
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