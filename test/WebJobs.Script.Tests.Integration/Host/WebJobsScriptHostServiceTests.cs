// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license Informationrmation.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Tests.TestFunctionHost;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Host
{
    public class WebJobsScriptHostServiceTests : IClassFixture<WebJobsScriptHostServiceTests.Fixture>
    {
        private readonly Fixture _fixture;

        public WebJobsScriptHostServiceTests(Fixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void InitializationLogs_AreEmitted()
        {
            // verify startup trace logs
            string[] expectedPatterns = new string[]
            {
                    "Information Reading host configuration file",
                    "Information Host configuration file read",
                    @"Information Generating ([0-9]+) job function\(s\)",
                    "Host initialization: ConsecutiveErrors=0, StartupCount=1",
                    @"Information Starting Host \(HostId=function-tests-node, InstanceId=(.*), Version=(.+), ProcessId=[0-9]+, AppDomainId=[0-9]+, Debug=False, FunctionsExtensionVersion=\)",
                    "Information Found the following functions:",
                    "Information The next 5 occurrences of the schedule will be:",
                    "Information Job host started",
                    "Error The following 1 functions are in error:"
            };

            IList<LogMessage> logs = _fixture.LoggerProvider.GetAllLogMessages();
            foreach (string pattern in expectedPatterns)
            {
                Assert.True(logs.Any(p => Regex.IsMatch($"{p.Level} {p.FormattedMessage}", pattern)), $"Expected trace event {pattern} not found.");
            }
        }

        public class Fixture : IAsyncLifetime
        {
            public ScriptApplicationHostOptions ApplicationOptions { get; set; } = new ScriptApplicationHostOptions();

            public TestLoggerProvider LoggerProvider { get; private set; }

            public IWebHost Host { get; private set; }

            public IScriptHostManager ScriptHostManager => Host.Services.GetService<IScriptHostManager>();

            public TestServer TestServer { get; private set; }

            public HttpClient HttpClient { get; private set; }

            public async Task DisposeAsync()
            {
                await Host.StopAsync();

                Host.Dispose();
            }

            public async Task InitializeAsync()
            {
                InitializeFiles();

                LoggerProvider = new TestLoggerProvider();

                var optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(ApplicationOptions);
                var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(optionsFactory, Array.Empty<IOptionsChangeTokenSource<ScriptApplicationHostOptions>>(), optionsFactory);

                TestServer = new TestServer(AspNetCore.WebHost.CreateDefaultBuilder()
                    .ConfigureLogging(b =>
                    {
                        b.AddProvider(LoggerProvider);
                    })
                    .UseStartup<Startup>()
                    .ConfigureServices(services =>
                    {
                        services.Replace(new ServiceDescriptor(typeof(IOptions<ScriptApplicationHostOptions>), new OptionsWrapper<ScriptApplicationHostOptions>(ApplicationOptions)));
                        services.Replace(new ServiceDescriptor(typeof(ISecretManager), new TestSecretManager()));
                        services.Replace(new ServiceDescriptor(typeof(IOptionsMonitor<ScriptApplicationHostOptions>), optionsMonitor));
                        services.AddSingleton<IConfigureBuilder<ILoggingBuilder>>(new DelegatedConfigureBuilder<ILoggingBuilder>(b => b.AddProvider(LoggerProvider)));
                        services.AddSingleton<IConfigureBuilder<IWebJobsBuilder>>(new DelegatedConfigureBuilder<IWebJobsBuilder>(b =>
                        {
                            b.AddAzureStorage();
                            b.Services.Configure<ScriptJobHostOptions>(o => o.Functions = new[] { "TimerTrigger", "Invalid" });
                        }));
                    }));

                var scriptConfig = TestServer.Host.Services.GetService<IOptions<ScriptJobHostOptions>>().Value;

                HttpClient = TestServer.CreateClient();
                HttpClient.BaseAddress = new Uri("https://localhost/");

                Host = TestServer.Host;

                await ScriptHostManager.DelayUntilHostReady();
            }

            private void InitializeFiles()
            {
                ApplicationOptions.ScriptPath = @"TestScripts\Node";
                ApplicationOptions.LogPath = Path.Combine(TestHelpers.FunctionsTestDirectory, "Logs", Guid.NewGuid().ToString(), @"Functions");
                ApplicationOptions.SecretsPath = Path.Combine(TestHelpers.FunctionsTestDirectory, "Secrets", Guid.NewGuid().ToString());

                // Create directories
                Directory.CreateDirectory(ApplicationOptions.LogPath);
                Directory.CreateDirectory(ApplicationOptions.SecretsPath);

                // Add some secret files (both old and valid)
                File.WriteAllText(Path.Combine(ApplicationOptions.SecretsPath, ScriptConstants.HostMetadataFileName), string.Empty);
                File.WriteAllText(Path.Combine(ApplicationOptions.SecretsPath, "WebHookTrigger.json"), string.Empty);
                File.WriteAllText(Path.Combine(ApplicationOptions.SecretsPath, "QueueTriggerToBlob.json"), string.Empty);
                File.WriteAllText(Path.Combine(ApplicationOptions.SecretsPath, "Foo.json"), string.Empty);
                File.WriteAllText(Path.Combine(ApplicationOptions.SecretsPath, "Bar.json"), string.Empty);
                File.WriteAllText(Path.Combine(ApplicationOptions.SecretsPath, "Invalid.json"), string.Empty);

                // Add some old file directories
                CreateTestFunctionLogs(ApplicationOptions.LogPath, "Foo");
                CreateTestFunctionLogs(ApplicationOptions.LogPath, "Bar");
                CreateTestFunctionLogs(ApplicationOptions.LogPath, "Baz");
                CreateTestFunctionLogs(ApplicationOptions.LogPath, "Invalid");
            }

            private void CreateTestFunctionLogs(string logRoot, string functionName)
            {
                string functionLogPath = Path.Combine(logRoot, functionName);
                FileWriter fileWriter = new FileWriter(functionLogPath);
                fileWriter.AppendLine("Test log message");
                fileWriter.Flush();
            }
        }
    }
}
