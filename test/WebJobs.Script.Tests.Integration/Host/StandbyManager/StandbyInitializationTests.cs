//// Copyright (c) .NET Foundation. All rights reserved.
//// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StandbyInitializationTests
    {
        [Fact]
        public async Task IsPlaceholderMode_ThroughoutInitialization_EvaluatesCorrectly()
        {
            StandbyManager.ResetChangeToken();

            string standbyPath = Path.Combine(Path.GetTempPath(), "functions", "standby", "wwwroot");
            string specializedScriptRoot = @"TestScripts\CSharp";
            string scriptRootConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.ScriptPath));

            var settings = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" },
                { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
             };

            var environment = new TestEnvironment(settings);
            var loggerProvider = new TestLoggerProvider();

            var builder = Program.CreateWebHostBuilder()
                .ConfigureLogging(b =>
                {
                    b.AddProvider(loggerProvider);
                })
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { scriptRootConfigPath, specializedScriptRoot }
                    });
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
                    s.PostConfigure<ScriptJobHostOptions>(o =>
                    {
                        // Only load the function we care about, but not during standby
                        if (o.RootScriptPath != standbyPath)
                        {
                            o.Functions = new[]
                            {
                                "HttpTrigger-Dynamic"
                            };
                        }
                    });
                });

            var server = new TestServer(builder);
            var client = server.CreateClient();

            // Force the specialization middleware to run       
            HttpResponseMessage response = await InvokeFunction(client);
            response.EnsureSuccessStatusCode();

            string log = loggerProvider.GetLog();
            Assert.Contains("Creating StandbyMode placeholder function directory", log);
            Assert.Contains("Starting host specialization", log);

            // Make sure this was registered.
            var hostedServices = server.Host.Services.GetServices<IHostedService>();
            Assert.Contains(hostedServices, p => p is StandbyInitializationService);
        }

        private static void Specialize(IEnvironment environment)
        {
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, "dotnet");
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
                RequestUri = new Uri("http://localhost/api/httptrigger-dynamic"),
                Method = HttpMethod.Post,
                Content = new StringContent(input.ToString())
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            return await client.SendAsync(request);
        }
    }
}
