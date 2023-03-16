// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;

using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class FunctionsHostingConfigOptionsTest
    {
        [Theory]
        [InlineData("FEATURE1", "value1")]
        [InlineData("FeAtUrE1", "value1")]
        [InlineData("feature1", "value1")]
        [InlineData("featuree1", null)]
        public async Task Case_Insensitive(string key, string expectedValue)
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                IHost host = GetScriptHostBuilder(Path.Combine(tempDir.Path, "settings.txt"), $"feature1=value1,feature2=value2").Build();
                var testService = host.Services.GetService<TestService>();

                _ = Task.Run(async () =>
                {
                    await TestHelpers.Await(() =>
                    {
                        return testService.Options.Value.GetFeature(key) == expectedValue;
                    });
                    await host.StopAsync();
                });

                await host.RunAsync();
                Assert.Equal(testService.Options.Value.GetFeature(key), expectedValue);
            }
        }

        [Fact]
        public async Task Inject_Succeded()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                IHost host = GetScriptHostBuilder(Path.Combine(tempDir.Path, "settings.txt"), $"feature1=value1,feature2=value2").Build();
                var testService = host.Services.GetService<TestService>();

                _ = Task.Run(async () =>
                {
                    await TestHelpers.Await(() =>
                    {
                        return testService.Options.Value.GetFeature("feature1") == "value1";
                    });
                    await host.StopAsync();
                });

                await host.RunAsync();
                Assert.Equal(testService.Options.Value.GetFeature("feature1"), "value1");
            }
        }

        [Fact]
        public async Task OnChange_Fires_OnFileChange()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                IHost host = GetScriptHostBuilder(fileName, $"feature1=value1,feature2=value2").Build();
                var testService = host.Services.GetService<TestService>();

                await host.StartAsync();

                await Task.Delay(1000);
                File.WriteAllText(fileName, $"feature1=value1_updated");
                await TestHelpers.Await(() =>
                {
                    return testService.Monitor.CurrentValue.GetFeature("feature1") == "value1_updated";
                });
            }
        }

        [Fact]
        public async Task OnChange_Fires_OnFileDelete()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                IHost host = GetScriptHostBuilder(fileName, $"feature1=value1,feature2=value2").Build();
                var testService = host.Services.GetService<TestService>();

                await host.StartAsync();

                await Task.Delay(1000);
                File.Delete(fileName);

                await TestHelpers.Await(() =>
                {
                    return testService.Monitor.CurrentValue.GetFeature("feature1") == null;
                });
                await host.StopAsync();
            }
        }

        [Fact]
        public async Task OnChange_Fires_OnFileCreate()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                IHost host = GetScriptHostBuilder(fileName, string.Empty).Build();
                var testService = host.Services.GetService<TestService>();

                await host.StartAsync();
                await Task.Delay(1000);

                File.WriteAllText(fileName, $"feature1=value1_updated");

                await TestHelpers.Await(() =>
                {
                    return testService.Monitor.CurrentValue.GetFeature("feature1") == "value1_updated";
                });
            }
        }

        internal static IHostBuilder GetScriptHostBuilder(string fileName, string fileContent)
        {
            if (!string.IsNullOrEmpty(fileContent))
            {
                File.WriteAllText(fileName, fileContent);
            }

            TestEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsPlatformConfigFilePath, fileName);

            IHost webHost = new HostBuilder()
                .ConfigureAppConfiguration((builderContext, config) =>
                {
                    config.Add(new FunctionsHostingConfigSource(environment));
                })
                .ConfigureServices((context, services) =>
                {
                    services.ConfigureOptions<FunctionsHostingConfigOptionsSetup>();
                    services.Configure<FunctionsHostingConfigOptions>(context.Configuration.GetSection(ScriptConstants.FunctionsHostingConfigSectionName));
                }).Build();

            return new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<TestService>();
                })
                .ConfigureDefaultTestWebScriptHost(null, configureRootServices: (services) =>
                {
                    services.AddSingleton(webHost.Services.GetService<IOptions<FunctionsHostingConfigOptions>>());
                    services.AddSingleton(webHost.Services.GetService<IOptionsMonitor<FunctionsHostingConfigOptions>>());
                });
        }

        public class TestService
        {
            public TestService(IOptions<Config.FunctionsHostingConfigOptions> options, IOptionsMonitor<Config.FunctionsHostingConfigOptions> monitor)
            {
                Options = options;
                Monitor = monitor;
                monitor.OnChange((changedOptions) =>
                {
                    Assert.Equal(changedOptions.GetFeature("feature1"), "value1_updated");
                });
            }

            public IOptions<Config.FunctionsHostingConfigOptions> Options { get; set; }

            public IOptionsMonitor<Config.FunctionsHostingConfigOptions> Monitor { get; set; }
        }
    }
}
