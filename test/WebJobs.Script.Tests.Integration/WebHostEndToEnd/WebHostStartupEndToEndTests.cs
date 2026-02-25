// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd;

public class WebHostStartupEndToEndTests
{
    private static readonly string _scriptRootConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.ScriptPath));

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

    [Fact]
    public async Task WebHostOptionsAreLogged_WithoutPlaceholderMode()
    {
        // Validates that WebHost-level options implementing IOptionsFormatter are logged
        // when the host starts directly without placeholder mode (no specialization).
        const string optionsCategory = "Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService";

        var loggerProvider = new TestLoggerProvider();
        var builder = CreateHostBuilder(loggerProvider, "FunctionExecutionContext");

        using var testServer = new TestServer(builder);
        var client = testServer.CreateClient();

        var response = await client.GetAsync("api/functionexecutioncontext");
        response.EnsureSuccessStatusCode();

        // Wait for the OptionsLoggingService to process all queued log entries.
        await TestHelpers.Await(() =>
            loggerProvider.GetAllLogMessages()
                .Any(m => string.Equals(m.Category, optionsCategory, StringComparison.Ordinal)
                       && m.FormattedMessage.StartsWith(nameof(HttpBodyControlOptions), StringComparison.Ordinal)));

        var allOptionsLogs = loggerProvider.GetAllLogMessages()
            .Where(m => string.Equals(m.Category, optionsCategory, StringComparison.Ordinal))
            .Select(m => m.FormattedMessage)
            .ToList();

        // WebHost-level options implementing IOptionsFormatter should be logged.
        TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(HttpBodyControlOptions));
        TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(ResponseCompressionOptions));
        TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(HostHealthMonitorOptions));
        TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(WorkerConfigurationResolverOptions));
        TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(LanguageWorkerOptions));
    }

    private static IWebHostBuilder CreateHostBuilder(TestLoggerProvider loggerProvider, params string[] functions)
    {
        var environment = new TestEnvironment(new Dictionary<string, string>
        {
            { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0" },
            { EnvironmentSettingNames.AzureWebsiteContainerReady, "1" },
        });

        return Program.CreateWebHostBuilder()
            .ConfigureLogging(b =>
            {
                b.AddProvider(loggerProvider);
                b.AddFilter<TestLoggerProvider>("Microsoft.Azure.WebJobs", LogLevel.Debug);
            })
            .ConfigureAppConfiguration(c =>
            {
                c.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { _scriptRootConfigPath, @"TestScripts\CSharp" }
                });
            })
            .ConfigureServices((bc, s) =>
            {
                s.AddSingleton<IEnvironment>(environment);
            })
            .ConfigureScriptHostServices(s =>
            {
                s.AddLogging(logging =>
                {
                    logging.AddProvider(loggerProvider);
                });

                s.PostConfigure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = functions;
                });
            });
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