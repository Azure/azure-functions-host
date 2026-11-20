using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Controllers
{
    [Trait(TestTraits.Group, TestTraits.NonE2EControllers)]
    public class WarmupScenarios : IAsyncLifetime
    {
        private static readonly TimeSpan SemaphoreWaitTimeout = TimeSpan.FromSeconds(30);

        private TestFunctionHost _testHost;
        private TestScopedEnvironmentVariable _workerRuntime;
        private static SemaphoreSlim _hostSetup;
        private static SemaphoreSlim _hostBuild;

        public WarmupScenarios()
        {
            _hostSetup = new SemaphoreSlim(1, 1);
            _hostBuild = new SemaphoreSlim(0, 2);
        }

        [Fact]
        public async Task TestWarmupEndPoint_WhenHostStarts()
        {
            _workerRuntime = new TestScopedEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, RpcWorkerConstants.NodeLanguageWorkerName);
            string testScriptPath = Path.Combine("TestScripts", "HostApi");
            string testLogPath = Path.Combine(TestHelpers.FunctionsTestDirectory, "Logs", Guid.NewGuid().ToString(), @"Functions");
            var settings = new Dictionary<string, string>()
            {
                ["WEBSITE_SKU"] = "ElasticPremium",
                [EnvironmentSettingNames.FunctionWorkerRuntime] = RpcWorkerConstants.NodeLanguageWorkerName
            };
            var testEnvironment = new TestEnvironment(settings);

            _testHost = new TestFunctionHost(testScriptPath, testLogPath,
            configureWebHostServices: services =>
            {
                services.AddSingleton<IScriptHostBuilder, PausingScriptHostBuilder>();
                services.AddSingleton<IEnvironment>(testEnvironment);
                services.AddSingleton<IConfigureBuilder<IWebJobsBuilder>>(new DelegatedConfigureBuilder<IWebJobsBuilder>(b =>
                {
                    b.UseHostId("1234");
                    b.Services.Configure<ScriptJobHostOptions>(o => o.Functions = new[] { "HttpTrigger" });
                }));
            });

            // Make sure host started properly and drain the semaphore count released
            // by inital start
            Assert.True(_hostBuild.Wait(SemaphoreWaitTimeout), "Host failed to start");

            string uri = "admin/warmup";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            HttpResponseMessage response = await _testHost.HttpClient.SendAsync(request);

            // Better be successful
            Assert.True(response.IsSuccessStatusCode, "Warmup endpoint did not return a success status. " +
                $"Instead found {response.StatusCode}. " +
                string.Join(Environment.NewLine, _testHost.GetScriptHostLogMessages().Select(p => $"{p.FormattedMessage} {p.Exception}")));
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            if (_testHost is not null)
            {
                await _testHost.DisposeAsync();
            }

            _workerRuntime?.Dispose();
        }

        private class PausingScriptHostBuilder : IScriptHostBuilder
        {
            private readonly DefaultScriptHostBuilder _inner;
            private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;

            public PausingScriptHostBuilder(IOptionsMonitor<ScriptApplicationHostOptions> options, IServiceProvider rootServiceProvider, IServiceCollection rootServices)
            {
                _inner = new DefaultScriptHostBuilder(options, rootServices, rootServiceProvider);
                _options = options;
            }

            public IHost BuildHost(bool skipHostStartup, bool skipHostConfigurationParsing)
            {
                IHost host = _inner.BuildHost(skipHostStartup, skipHostConfigurationParsing);

                _hostBuild.Release();
                Assert.True(_hostSetup.Wait(SemaphoreWaitTimeout));
                return host;
            }
        }
    }
}
