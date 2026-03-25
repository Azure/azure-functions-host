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
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    public class ControllerScenarioTestFixture : IAsyncLifetime, IDisposable
    {
        private ScriptSettingsManager _settingsManager;
        private HttpConfiguration _config;

        public ScriptApplicationHostOptions HostOptions { get; private set; }

        public HttpClient HttpClient { get; set; }

        public IHost Host { get; set; }

        protected virtual void ConfigureWebHostBuilder(IWebHostBuilder webHostBuilder) { }

        public void Dispose()
        {
            HttpClient?.Dispose();

            if (Host is not null)
            {
                try
                {
                    Host.StopAsync().GetAwaiter().GetResult();
                }
                catch
                {
                }

                Host.Dispose();
            }
        }

        public virtual async Task InitializeAsync()
        {
            _config = new HttpConfiguration();
            _settingsManager = ScriptSettingsManager.Instance;

            var webHostBuilder = Program.CreateHostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();

                    ConfigureWebHostBuilder(webBuilder);
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IDiagnosticEventRepository, DiagnosticEventNullRepository>();
                    services.AddSingleton<IDiagnosticEventRepositoryFactory, TestDiagnosticEventRepositoryFactory>();
                    services.PostConfigure<ScriptApplicationHostOptions>(o=>
                    {
                        o.IsSelfHost = true;
                        o.ScriptPath = Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "sample", "csharp");
                        o.LogPath = Path.Combine(Path.GetTempPath(), @"Functions", "ControllerScenarioTests");
                        o.SecretsPath = Path.Combine(Path.GetTempPath(), @"FunctionsTests\Secrets");
                        o.HasParentScope = true;

                        HostOptions = o;
                    });
                });

            Host = webHostBuilder.Build();
            await Host.StartAsync();

            HttpClient = Host.GetTestClient();
            HttpClient.BaseAddress = new Uri("https://localhost/");

            var manager = Host.Services.GetService<IScriptHostManager>();
            await manager.DelayUntilHostReadyAsync();
        }

        public Task DisposeAsync()
        { 
            return Task.CompletedTask;
        }
    }
}