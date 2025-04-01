// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
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

        [Theory]
        [InlineData("True", false, true)]
        [InlineData("False", true, false)]
        [InlineData("tRuE", false, true)]
        [InlineData(" true ", false, true)]
        [InlineData("1", false, true)]
        [InlineData("0", true, false)]
        [InlineData("-2", false, true)] // any non-zero int is true
        [InlineData("unparseable", false, false)]
        [InlineData(null, true, true)]
        public void GetFeatureAsBooleanOrDefault(string featureValue, bool defaultValue, bool expected)
        {
            string feature = "TestFeature";
            var options = new FunctionsHostingConfigOptions();

            if (featureValue != null)
            {
                options.Features.Add(feature, featureValue);
            }

            Assert.Equal(expected, options.GetFeatureAsBooleanOrDefault(feature, defaultValue));
        }

        [Fact]
        public void Property_Validation()
        {
            // Doing this all in one test case (not using Theory) so that we can ensure we have at least one test for every property.
            // Note: For legacy purposes (we used to call Configuration.Bind() on this object), some properties whose ScmHostingConfig key and
            //       property name match exactly need to support "True/False".
            //       It is recommended that new properties only look at "1/0" for their setting.
            var testCases = new List<(string PropertyName, string ConfigValue, object Expected)>
            {
                (nameof(FunctionsHostingConfigOptions.DisableLinuxAppServiceExecutionDetails), "DisableLinuxExecutionDetails=1", true),
                (nameof(FunctionsHostingConfigOptions.DisableLinuxAppServiceLogBackoff), "DisableLinuxLogBackoff=1", true),

                // Supports True/False/1/0
                (nameof(FunctionsHostingConfigOptions.EnableOrderedInvocationMessages), "EnableOrderedInvocationMessages=True", true),
                (nameof(FunctionsHostingConfigOptions.EnableOrderedInvocationMessages), "EnableOrderedInvocationMessages=1", true),
                (nameof(FunctionsHostingConfigOptions.EnableOrderedInvocationMessages), "EnableOrderedInvocationMessages=unparseable", true), // default
                (nameof(FunctionsHostingConfigOptions.EnableOrderedInvocationMessages), string.Empty, true), // default

                (nameof(FunctionsHostingConfigOptions.FunctionsWorkerDynamicConcurrencyEnabled), "FUNCTIONS_WORKER_DYNAMIC_CONCURRENCY_ENABLED=1", true),
                (nameof(FunctionsHostingConfigOptions.MaximumBundleV3Version), "FunctionRuntimeV4MaxBundleV3Version=teststring", "teststring"),
                (nameof(FunctionsHostingConfigOptions.MaximumBundleV4Version), "FunctionRuntimeV4MaxBundleV4Version=teststring", "teststring"),
                (nameof(FunctionsHostingConfigOptions.RevertWorkerShutdownBehavior), "REVERT_WORKER_SHUTDOWN_BEHAVIOR=1", true),

                // Supports True/False/1/0
                (nameof(FunctionsHostingConfigOptions.ShutdownWebhostWorkerChannelsOnHostShutdown), "ShutdownWebhostWorkerChannelsOnHostShutdown=False", false),
                (nameof(FunctionsHostingConfigOptions.ShutdownWebhostWorkerChannelsOnHostShutdown), "ShutdownWebhostWorkerChannelsOnHostShutdown=True", true),
                (nameof(FunctionsHostingConfigOptions.ShutdownWebhostWorkerChannelsOnHostShutdown), "ShutdownWebhostWorkerChannelsOnHostShutdown=1", true),
                (nameof(FunctionsHostingConfigOptions.ShutdownWebhostWorkerChannelsOnHostShutdown), "ShutdownWebhostWorkerChannelsOnHostShutdown=unparseable", true), // default
                (nameof(FunctionsHostingConfigOptions.ShutdownWebhostWorkerChannelsOnHostShutdown), string.Empty, true), // default

                // Supports True/False/1/0
                (nameof(FunctionsHostingConfigOptions.SwtIssuerEnabled), "SwtIssuerEnabled=False", false),
                (nameof(FunctionsHostingConfigOptions.SwtIssuerEnabled), "SwtIssuerEnabled=True", true),
                (nameof(FunctionsHostingConfigOptions.SwtIssuerEnabled), "SwtIssuerEnabled=0", false),
                (nameof(FunctionsHostingConfigOptions.SwtIssuerEnabled), "SwtIssuerEnabled=unparseable", true), //default
                (nameof(FunctionsHostingConfigOptions.SwtIssuerEnabled), string.Empty, true), // default

                (nameof(FunctionsHostingConfigOptions.ThrowOnMissingFunctionsWorkerRuntime), "THROW_ON_MISSING_FUNCTIONS_WORKER_RUNTIME=1", true),
                (nameof(FunctionsHostingConfigOptions.WorkerIndexingDisabledApps), "WORKER_INDEXING_DISABLED_APPS=teststring", "teststring"),
                (nameof(FunctionsHostingConfigOptions.WorkerIndexingEnabled), "WORKER_INDEXING_ENABLED=1", true),
                (nameof(FunctionsHostingConfigOptions.WorkerRuntimeStrictValidationEnabled), "WORKER_RUNTIME_STRICT_VALIDATION_ENABLED=1", true),

                (nameof(FunctionsHostingConfigOptions.InternalAuthApisAllowList), "InternalAuthApisAllowList=|", "|"),
                (nameof(FunctionsHostingConfigOptions.InternalAuthApisAllowList), "InternalAuthApisAllowList=/admin/host/foo|/admin/host/bar", "/admin/host/foo|/admin/host/bar"),

                (nameof(FunctionsHostingConfigOptions.IsTestDataSuppressionEnabled), "EnableTestDataSuppression=1", true)
            };

            // use reflection to ensure that we have a test that uses every value exposed on FunctionsHostingConfigOptions
            // (except for Features, which does not get bound).
            var props = typeof(FunctionsHostingConfigOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(p => p.Name != nameof(FunctionsHostingConfigOptions.Features));

            foreach (var prop in props)
            {
                // make sure all props are internal to prevent inadverntent binding in the future
                if (prop.GetGetMethod() is not null || prop.GetSetMethod() is not null)
                {
                    throw new InvalidOperationException($"{prop.Name} is public. All properties on this object should be internal.");
                }

                // just make sure we have at least one test per prop
                var expected = testCases.FirstOrDefault(p => p.PropertyName == prop.Name);
                if (expected == default)
                {
                    throw new InvalidOperationException($"The property {prop.Name} is not set up to be validated. Please add at least one case in this test.");
                }
            }

            foreach (var (propertyName, configValue, expected) in testCases)
            {
                using TempDirectory tempDir = new();

                IHost host = GetScriptHostBuilder(Path.Combine(tempDir.Path, "settings.txt"), configValue).Build();
                var testService = host.Services.GetService<TestService>();

                var prop = props.Single(p => p.Name == propertyName);
                var actual = prop.GetValue(testService.Options.Value);
                try
                {
                    Assert.Equal(expected, actual);
                }
                catch (Exception)
                {
                    // provide a better failure message
                    Assert.True(false, $"{prop.Name} failure ('{configValue}'). Expected: {expected}. Actual: {actual}.");
                }
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

        [Fact]
        public void SwtIssuerEnabled_ReturnsExpectedValue()
        {
            FunctionsHostingConfigOptions options = new FunctionsHostingConfigOptions();

            // defaults to true
            Assert.True(options.SwtIssuerEnabled);

            // returns true when explicitly enabled
            options.Features[ScriptConstants.HostingConfigSwtIssuerEnabled] = "1";
            Assert.True(options.SwtIssuerEnabled);

            // returns false when disabled
            options.Features[ScriptConstants.HostingConfigSwtIssuerEnabled] = "0";
            Assert.False(options.SwtIssuerEnabled);
        }

        [Fact]
        public void InternalAuthApisAllowList_ReturnsExpectedValue()
        {
            FunctionsHostingConfigOptions options = new FunctionsHostingConfigOptions();

            Assert.Null(options.InternalAuthApisAllowList);

            options.InternalAuthApisAllowList = string.Empty;
            Assert.Equal(string.Empty, options.InternalAuthApisAllowList);

            options.InternalAuthApisAllowList = "/admin/host/synctriggers|/admin/host/status";
            Assert.Equal("/admin/host/synctriggers|/admin/host/status", options.InternalAuthApisAllowList);
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
                    WebHostServiceCollectionExtensions.AddHostingConfigOptions(services, context.Configuration);
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
