// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait(TestTraits.Group, TestTraits.NonE2ESpecialization)]
    public class StandbyInitializationTests
    {
        [Fact]
        public async Task IsPlaceholderMode_ThroughoutInitialization_EvaluatesCorrectly()
        {
            StandbyManager.ResetChangeToken();

            string standbyPath = Path.Combine(Path.GetTempPath(), "functions", "standby", "wwwroot");
            string specializedScriptRoot = @"TestScripts\NoFunction";
            string scriptRootConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.ScriptPath));
            string logPathConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.LogPath));

            var settings = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" },
                { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
             };

            var environment = new TestEnvironment(settings);
            var loggerProvider = new TestLoggerProvider();

            var builder = Program.CreateHostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder.UseTestServer();
                })
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { scriptRootConfigPath, specializedScriptRoot },
                        { logPathConfigPath, Path.Combine(Path.GetTempPath(), "Functions", "StandbyInitializationTests") }
                    });
                })
                .ConfigureLogging(b =>
                {
                    b.AddProvider(loggerProvider);
                })
                .ConfigureServices((bc, s) =>
                {
                    s.AddSingleton<IEnvironment>(environment);

                    // Simulate the environment becoming specialized after these options have been
                    // initialized with standby paths.
                    s.AddOptions<ScriptApplicationHostOptions>()
                        .PostConfigure<IEnvironment>((o, e) =>
                        {
                            Specialize(e);
                        });
                })
                .ConfigureScriptHostServices(s =>
                {
                    s.AddSingleton<IFunctionProvider>(services =>
                    {
                        var metadata = CodelessEndToEndTests_Data.GetCSharpSampleMetadata("SpecializedExtension");
                        metadata.Bindings[0] = BindingMetadata.Create(JObject.Parse("""
                            {
                                "authLevel": "anonymous",
                                "type": "httpTrigger",
                                "direction": "in",
                                "name": "req",
                                "methods": ["post"]
                            }
                            """));
                        var provider = new Mock<IFunctionProvider>();
                        provider.SetupGet(p => p.FunctionErrors)
                            .Returns(ImmutableDictionary<string, ImmutableArray<string>>.Empty);
                        provider.Setup(p => p.GetFunctionMetadataAsync()).ReturnsAsync(() =>
                            string.Equals(services.GetRequiredService<IOptions<ScriptJobHostOptions>>().Value.RootScriptPath,
                                standbyPath, StringComparison.OrdinalIgnoreCase)
                                ? ImmutableArray<FunctionMetadata>.Empty
                                : ImmutableArray.Create(metadata));

                        return provider.Object;
                    });
                    s.PostConfigure<ScriptJobHostOptions>(o =>
                    {
                        // Only load the function we care about, but not during standby
                        if (!string.Equals(o.RootScriptPath, standbyPath, StringComparison.OrdinalIgnoreCase))
                        {
                            o.Functions =
                            [
                                "SpecializedExtension"
                            ];
                        }
                    });
                });

            var host = builder.Build();
            try
            {
                await host.StartAsync();
                var client = host.GetTestClient();

                // Force the specialization middleware to run       
                HttpResponseMessage response = await InvokeFunction(client);
                response.EnsureSuccessStatusCode();
                Assert.Contains("Codeless Provider ran a function successfully", await response.Content.ReadAsStringAsync());

                string log = loggerProvider.GetLog();
                Assert.Contains("Creating StandbyMode placeholder function directory", log);
                Assert.Contains("Starting host specialization", log);

                // Make sure this was registered.
                var hostedServices = host.Services.GetServices<IHostedService>();
                Assert.Contains(hostedServices, p => p is StandbyInitializationService);
            }
            finally
            {
                await host.StopAsync();
                host.Dispose();
            }
        }

        private static void Specialize(IEnvironment environment)
        {
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, RpcWorkerConstants.DotNetIsolatedLanguageWorkerName);
        }

        private static async Task<HttpResponseMessage> InvokeFunction(HttpClient client)
        {
            var input = new JObject
            {
                { "name", "he" },
                { "location", "here" }
            };
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri("http://localhost/api/SpecializedExtension"),
                Method = HttpMethod.Post,
                Content = new StringContent(input.ToString())
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            return await client.SendAsync(request);
        }
    }
}
