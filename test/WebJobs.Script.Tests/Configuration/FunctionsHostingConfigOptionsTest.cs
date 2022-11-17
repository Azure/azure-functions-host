// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
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
        [Fact]
        public async Task Inject_Succeded()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                IHost host = GetHostBuilder(Path.Combine(tempDir.Path, "settings.txt"), $"feature1=value1,feature2=value2").Build();
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
                IHost host = GetHostBuilder(fileName, $"feature1=value1,feature2=value2").Build();
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
                IHost host = GetHostBuilder(fileName, $"feature1=value1,feature2=value2").Build();
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
                IHost host = GetHostBuilder(fileName, string.Empty).Build();
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

        internal static IHostBuilder GetHostBuilder(string fileName, string fileContent)
        {
            if (!string.IsNullOrEmpty(fileContent))
            {
                File.WriteAllText(fileName, fileContent);
            }

            TestEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsPlatformConfigFilePath, fileName);

            return new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<TestService>();
                    services.ConfigureOptions<FunctionsHostingConfigOptionsSetup>();
                    services.AddSingleton<IOptionsChangeTokenSource<FunctionsHostingConfigOptions>, ConfigurationChangeTokenSource<FunctionsHostingConfigOptions>>();
                })
                .ConfigureDefaultTestWebScriptHost(null, configureRootServices: (services) =>
                {
                    services.AddSingleton<IEnvironment>(environment);
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
