// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd;

public class WebHostStartupEndToEndTests
{
    [Fact]
    public async Task TransientError_DuringHostBuild_DoesNotDeadlock()
    {
        bool thrownOnce = false;
        void ThrowOnFirstBuild()
        {
            if (!thrownOnce)
            {
                // Simulate a transient error during first host build
                thrownOnce = true;
                throw new InvalidOperationException("Simulated transient error during host build.");
            }
        }

        var fixture = new WebHostStartupEndToEndTestFixture(ThrowOnFirstBuild);

        try
        {
            await fixture.InitializeAsync();

            // This should recover as the second call to BuildHost should succeed
            await TestHelpers.Await(async () =>
            {
                var result = await fixture.Host.HttpClient.GetAsync("/api/HttpRequestDataFunction");
                return result.IsSuccessStatusCode && await result.Content.ReadAsStringAsync() == "Welcome to Azure Functions!";

            }, 10000, userMessageCallback: fixture.Host.GetLog);

            var debugMsg = fixture.Host.GetWebHostLogMessages("Microsoft.Azure.WebJobs.Script.WorkerFunctionMetadataProvider")
                .Where(m => m.Level == Microsoft.Extensions.Logging.LogLevel.Debug)
                .Where(m => m.FormattedMessage.StartsWith("JobHost is starting with state"));
            Assert.Single(debugMsg);
            Assert.Contains("'Error'", debugMsg.Single().FormattedMessage);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private class WebHostStartupEndToEndTestFixture : EndToEndTestFixture
    {
        private readonly Action _scriptHostBuildInterceptor;

        public WebHostStartupEndToEndTestFixture(Action scriptHostBuildInterceptor = null)
            : base(@$"..\..\DotNetIsolated60\{TestHelpers.BuildConfig}", "WebHostStartupEndToEndTests", "dotnet-isolated")
        {
            _scriptHostBuildInterceptor = scriptHostBuildInterceptor;
        }

        protected override Task CreateTestStorageEntities() => Task.CompletedTask;

        public override void ConfigureWebHost(IServiceCollection rootServices)
        {
            rootServices.AddSingleton<IScriptHostBuilder>(rootProvider =>
            {
                var appHostOptions = rootProvider.GetService<IOptionsMonitor<ScriptApplicationHostOptions>>();

                IHost Intercept(IScriptHostBuilder builder, bool skipHostStartup, bool skipHostConfigurationParsing)
                {
                    _scriptHostBuildInterceptor?.Invoke();

                    return builder.BuildHost(skipHostStartup, skipHostConfigurationParsing);
                }

                return new InterceptingScriptHostBuilder(appHostOptions, rootProvider, rootServices, Intercept);
            });
        }
    }
}