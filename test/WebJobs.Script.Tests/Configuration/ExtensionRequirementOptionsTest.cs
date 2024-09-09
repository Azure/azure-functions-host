// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.AppService.Proxy.Runtime.Configuration.Policies;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;

using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ExtensionRequirementOptionsTest
    {
        [Fact]
        public async Task BundleAndExtensionRequired_ReturnsValidConfiguration()
        {
            var testPath = Path.GetDirectoryName(new Uri(typeof(ExtensionRequirementOptionsTest).Assembly.Location).LocalPath);
            string filePath = Path.Combine(testPath, "TestFixture", "ExtensionRequirementOptionsTest", "FunctionsHostingEnvironmentConfig.json");
            var bundlconfig = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.FunctionsHostingEnvironmentConfigFilePath, filePath }
            };

            using (new TestScopedEnvironmentVariable(bundlconfig))
            {
                IHost host = GetScriptHostBuilder(filePath).Build();
                var testService = host.Services.GetService<TestService>();

                _ = Task.Run(async () =>
                {
                    await TestHelpers.Await(() =>
                    {
                        return testService.Options.Value.Bundles.ElementAt(0).Id == "Microsoft.Azure.Functions.ExtensionBundle";
                    });
                    await host.StopAsync();
                });

                await host.RunAsync();
                Assert.Equal(testService.Options.Value.Bundles.ElementAt(0).Id, "Microsoft.Azure.Functions.ExtensionBundle");
                Assert.Equal(testService.Options.Value.Bundles.ElementAt(0).MinimumVersion, "4.12.0");
                Assert.Equal(testService.Options.Value.Extensions.ElementAt(0).PackageName, "Microsoft.Azure.DurableTask.Netherite.AzureFunctions");
            }
        }

        [Fact]
        public async Task FileNotPresent_ReturnsNull()
        {
            var testPath = Path.GetDirectoryName(new Uri(typeof(ExtensionRequirementOptionsTest).Assembly.Location).LocalPath);
            var filePath = Path.Combine(testPath, "TestFixture", "ExtensionRequirementOptionsTest", "FileDoesNotExist.json");
            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.FunctionsHostingEnvironmentConfigFilePath, filePath))
            {
                IHost host = GetScriptHostBuilder(filePath).Build();
                var testService = host.Services.GetService<TestService>();

                _ = Task.Run(async () =>
                {
                    await TestHelpers.Await(() =>
                    {
                        return testService.Options.Value.Bundles == null && testService.Options.Value.Extensions == null;
                    });
                    await host.StopAsync();
                });

                await host.RunAsync();
            }
        }

        [Fact]
        public async Task EnvironmentVariableNotConfigured_ReturnsEmptyOptions()
        {
            IHost host = GetScriptHostBuilder().Build();
            var testService = host.Services.GetService<TestService>();

            _ = Task.Run(async () =>
            {
                await TestHelpers.Await(() =>
                {
                    return testService.Options.Value.Bundles == null && testService.Options.Value.Extensions == null;
                });
                await host.StopAsync();
            });

            await host.RunAsync();
        }

        [Fact]
        public async Task OnlyBundleRequired_ReturnsBundleConfig()
        {
            var testPath = Path.GetDirectoryName(new Uri(typeof(ExtensionRequirementOptionsTest).Assembly.Location).LocalPath);
            var filePath = Path.Combine(testPath, "TestFixture", "ExtensionRequirementOptionsTest", "FunctionsHostingEnvironmentConfig_bundlesOnly.json");
            var bundlconfig = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.FunctionsHostingEnvironmentConfigFilePath, filePath }
            };
            using (new TestScopedEnvironmentVariable(bundlconfig))
            {
                IHost host = GetScriptHostBuilder(filePath).Build();
                var testService = host.Services.GetService<TestService>();

                _ = Task.Run(async () =>
                {
                    await TestHelpers.Await(() =>
                    {
                        return testService.Options.Value.Bundles.ElementAt(0).Id == "Microsoft.Azure.Functions.ExtensionBundle";
                    });
                    await host.StopAsync();
                });

                await host.RunAsync();
                Assert.Equal(testService.Options.Value.Bundles.ElementAt(0).Id, "Microsoft.Azure.Functions.ExtensionBundle");
                Assert.Equal(testService.Options.Value.Bundles.ElementAt(0).MinimumVersion, "4.12.0");
            }
        }

        [Fact]
        public async Task EmpytJsonFile_ReturnsEmptyOptions()
        {
            var testPath = Path.GetDirectoryName(new Uri(typeof(ExtensionRequirementOptionsTest).Assembly.Location).LocalPath);
            var filePath = Path.Combine(testPath, "TestFixture", "ExtensionRequirementOptionsTest", "EmptyFile.json");
            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.FunctionsHostingEnvironmentConfigFilePath, filePath))
            {
                IHost host = GetScriptHostBuilder(filePath).Build();
                var testService = host.Services.GetService<TestService>();

                _ = Task.Run(async () =>
                {
                    await TestHelpers.Await(() =>
                    {
                        return testService.Options.Value.Bundles == null;
                    });
                    await host.StopAsync();
                });

                await host.RunAsync();
            }
        }

        [Fact]
        public async Task OnlyExtensionsRequired_ReturnsExtensionConfig()
        {
            var testPath = Path.GetDirectoryName(new Uri(typeof(ExtensionRequirementOptionsTest).Assembly.Location).LocalPath);
            var filePath = Path.Combine(testPath, "TestFixture", "ExtensionRequirementOptionsTest", "FunctionsHostingEnvironmentConfig_extensionsOnly.json");
            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.FunctionsHostingEnvironmentConfigFilePath, filePath))
            {
                IHost host = GetScriptHostBuilder(filePath).Build();
                var testService = host.Services.GetService<TestService>();

                _ = Task.Run(async () =>
                {
                    await TestHelpers.Await(() =>
                    {
                        return testService.Options.Value.Extensions.ElementAt(0).PackageName == "Microsoft.Azure.DurableTask.Netherite.AzureFunctions";
                    });
                    await host.StopAsync();
                });

                await host.RunAsync();
                Assert.Equal(testService.Options.Value.Extensions.ElementAt(0).PackageName, "Microsoft.Azure.DurableTask.Netherite.AzureFunctions");
            }
        }

        internal static IHostBuilder GetScriptHostBuilder(string fileName = null)
        {
            TestEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsHostingEnvironmentConfigFilePath, fileName);
            IHost webHost = new HostBuilder()
                .ConfigureAppConfiguration((builderContext, config) =>
                {
                    config.Add(new FunctionsHostingEnvironmentConfigSource(environment));
                })
                .ConfigureServices((context, services) =>
                {
                    services.ConfigureOptions<ExtensionRequirementOptionsSetup>();
                }).Build();

            return new HostBuilder()
                .ConfigureAppConfiguration((builderContext, config) =>
                {
                    config.Add(new FunctionsHostingEnvironmentConfigSource(SystemEnvironment.Instance));
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(webHost.Services.GetService<IOptions<ExtensionRequirementOptions>>());
                    services.AddSingleton<TestService>();
                })
                .ConfigureDefaultTestWebScriptHost(null, configureRootServices: (services) =>
                {
                    services.AddSingleton(webHost.Services.GetService<IOptions<ExtensionRequirementOptions>>());
                });
        }

        public class TestService
        {
            public TestService(IOptions<Config.ExtensionRequirementOptions> options)
            {
                Options = options;
            }

            public IOptions<Config.ExtensionRequirementOptions> Options { get; set; }
        }
    }
}
