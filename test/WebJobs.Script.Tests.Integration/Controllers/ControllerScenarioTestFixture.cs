// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    public class ControllerScenarioTestFixture : IAsyncLifetime, IDisposable
    {
        private ScriptSettingsManager _settingsManager;
        private HttpConfiguration _config;

        public ScriptApplicationHostOptions HostOptions { get; private set; }

        public HttpClient HttpClient { get; set; }

        public TestServer HttpServer { get; set; }

        protected virtual void ConfigureWebHostBuilder(IWebHostBuilder webHostBuilder)
        {
            webHostBuilder.ConfigureServices(c => c.AddSingleton(HostOptions));
        }

        public void Dispose()
        {
            HttpServer?.Dispose();
            HttpClient?.Dispose();
        }

        public virtual async Task InitializeAsync()
        {
            _config = new HttpConfiguration();
            _settingsManager = ScriptSettingsManager.Instance;

            HostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "sample", "csharp"),
                LogPath = Path.Combine(Path.GetTempPath(), @"Functions"),
                SecretsPath = Path.Combine(Path.GetTempPath(), @"FunctionsTests\Secrets"),
                HasParentScope = true
            };

            var optionsMonitor = TestHelpers.CreateOptionsMonitor(HostOptions);
            var webHostBuilder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.Replace(ServiceDescriptor.Singleton<IServiceProviderFactory<IServiceCollection>>(new WebHostServiceProviderFactory()));
                    services.AddSingleton<IOptions<ScriptApplicationHostOptions>>(new OptionsWrapper<ScriptApplicationHostOptions>(HostOptions));
                    services.Replace(new ServiceDescriptor(typeof(IOptionsMonitor<ScriptApplicationHostOptions>), optionsMonitor));
                })
                .ConfigureAppConfiguration((builderContext, config) =>
                {
                    config.Add(new WebScriptHostConfigurationSource
                    {
                        IsAppServiceEnvironment = SystemEnvironment.Instance.IsAppService(),
                        IsLinuxContainerEnvironment = SystemEnvironment.Instance.IsAnyLinuxConsumption()
                    });
                })
                .UseStartup<Startup>()
                .ConfigureAppConfiguration(c => c.AddEnvironmentVariables());

            ConfigureWebHostBuilder(webHostBuilder);

            HttpServer = new TestServer(webHostBuilder);
            HttpClient = HttpServer.CreateClient();
            HttpClient.BaseAddress = new Uri("https://localhost/");

            var manager = HttpServer.Host.Services.GetService<IScriptHostManager>();
            await manager.DelayUntilHostReady();
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }
}