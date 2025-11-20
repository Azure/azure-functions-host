// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Tests.WorkerConfigurationResolverTestsHelper;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class WorkerConfigurationResolverOptionsSetupTests
    {
        [Fact]
        public void Configure_WithEnvironmentValues_SetsCorrectValues()
        {
            var loggerFactory = GetTestLoggerFactory();
            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var hostingOptions = new FunctionsHostingConfigOptions();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                }).Build();

            var setup = new WorkerConfigurationResolverOptionsSetup(loggerFactory, configuration, testEnvironment, FileUtility.Instance, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();
            setup.Configure(options);

            Assert.Equal("/default/workers", options.WorkersRootDirPath);
        }

        [Fact]
        public void Configure_UpdatedConfiguration_SetsCorrectValues()
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var mockServiceProvider = new Mock<IServiceProvider>();
            var configuration = new ConfigurationBuilder().Build();
            var hostingOptions = new FunctionsHostingConfigOptions();
            var latestConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                }).Build();

            mockServiceProvider.Setup(sp => sp.GetService(typeof(IConfiguration))).Returns(latestConfiguration);
            mockScriptHostManager.As<IServiceProvider>()
                .Setup(sp => sp.GetService(typeof(IConfiguration)))
                .Returns(latestConfiguration);

            var setup = new WorkerConfigurationResolverOptionsSetup(loggerFactory, configuration, testEnvironment, FileUtility.Instance, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();
            setup.Configure(options);

            var logs = loggerProvider.GetAllLogMessages();

            Assert.Equal("/default/workers", options.WorkersRootDirPath);
            Assert.Single(logs.Where(l => l.FormattedMessage == "Found configuration section 'languageWorkers:workersDirectory' in JobHost."));
        }

        [Fact]
        public void Configure_WithConfiguration_SetsCorrectValues()
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var mockServiceProvider = new Mock<IServiceProvider>();
            var hostingOptions = new FunctionsHostingConfigOptions();
            var configuration = new ConfigurationBuilder()
                                    .AddInMemoryCollection(new Dictionary<string, string>
                                    {
                                        [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                                    })
                                    .Build();

            var latestConfiguration = new ConfigurationBuilder().Build();

            mockServiceProvider.Setup(sp => sp.GetService(typeof(IConfiguration))).Returns(latestConfiguration);
            mockScriptHostManager.As<IServiceProvider>()
                .Setup(sp => sp.GetService(typeof(IConfiguration)))
                .Returns(latestConfiguration);

            var setup = new WorkerConfigurationResolverOptionsSetup(loggerFactory, configuration, testEnvironment, FileUtility.Instance, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();
            setup.Configure(options);

            var logs = loggerProvider.GetAllLogMessages();

            Assert.Equal("/default/workers", options.WorkersRootDirPath);
            Assert.Single(logs.Where(l => l.FormattedMessage == "Found configuration section 'languageWorkers:workersDirectory' in WebHost."));
        }

        [Fact]
        public void Configure_WithNullConfigValues_SetsCorrectValues()
        {
            var testLoggerFactory = GetTestLoggerFactory();
            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var hostingOptions = new FunctionsHostingConfigOptions();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = null,
                }).Build();

            var setup = new WorkerConfigurationResolverOptionsSetup(testLoggerFactory, configuration, testEnvironment, FileUtility.Instance, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();
            setup.Configure(options);

            Assert.NotNull(options.WorkersRootDirPath);
            Assert.Contains("workers", options.WorkersRootDirPath);
        }

        [Fact]
        public void Configure_WorkerConfigurationResolverOptions()
        {
            var testLoggerFactory = GetTestLoggerFactory();
            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var configuration = new ConfigurationBuilder().Build();
            var hostingOptions = new FunctionsHostingConfigOptions();

            var setup = new WorkerConfigurationResolverOptionsSetup(testLoggerFactory, configuration, testEnvironment, FileUtility.Instance, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();
            setup.Configure(options);

            Assert.NotNull(options.WorkersRootDirPath);
            Assert.Contains("workers", options.WorkersRootDirPath);
        }

        [Fact]
        public void Format_SerializesOptionsToJson()
        {
            var options = new WorkerConfigurationResolverOptions
            {
                WorkersRootDirPath = "/test/workers",
                ReleaseChannel = "standard",
                WorkerRuntime = "node",
                IsPlaceholderModeEnabled = false,
                IsMultiLanguageWorkerEnvironment = true,
                ProbingPaths = new List<string> { "path1", "path2" },
                WorkersAvailableForResolution = new HashSet<string> { "node", "python" }.ToImmutableHashSet(),
                WorkerDescriptionOverrides = ImmutableDictionary<string, RpcWorkerDescription>.Empty.Add("node", new RpcWorkerDescription { Language = "node" }),
                IgnoredWorkerVersions = new Dictionary<string, HashSet<Version>>
                {
                    { "node", new HashSet<Version> { new Version("14.0.0"), new Version("16.0.0") } },
                    { "python", new HashSet<Version> { new Version("3.6"), new Version("3.7") } }
                }.ToImmutableDictionary(),
                IsDynamicWorkerResolutionEnabled = true
            };

            string json = options.Format();

            Assert.NotNull(json);
            Assert.NotEmpty(json);

            var jsonDocument = JsonDocument.Parse(json);
            Assert.NotNull(jsonDocument);

            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("WorkersRootDirPath", out var workersDirPathProperty));
            Assert.Equal("/test/workers", workersDirPathProperty.GetString());
            Assert.True(root.TryGetProperty("ReleaseChannel", out var releaseChannelProperty));
            Assert.Equal("standard", releaseChannelProperty.GetString());
            Assert.True(root.TryGetProperty("WorkerRuntime", out var workerRuntimeProperty));
            Assert.Equal("node", workerRuntimeProperty.GetString());
            Assert.True(root.TryGetProperty("IsPlaceholderModeEnabled", out var placeholderModeProperty));
            Assert.False(placeholderModeProperty.GetBoolean());
            Assert.True(root.TryGetProperty("IsMultiLanguageWorkerEnvironment", out var multiLangEnvProperty));
            Assert.True(multiLangEnvProperty.GetBoolean());
            Assert.True(root.TryGetProperty("ProbingPaths", out var probingPathsProperty));
            Assert.Equal(2, probingPathsProperty.GetArrayLength());
            Assert.True(root.TryGetProperty("WorkersAvailableForResolution", out var workersAvailableProperty));
            Assert.Equal(2, workersAvailableProperty.GetArrayLength());
            Assert.True(root.TryGetProperty("WorkerDescriptionOverrides", out var workerDescriptionOverridesProperty));
            Assert.NotNull(workerDescriptionOverridesProperty);
            Assert.True(root.TryGetProperty("IgnoredWorkerVersions", out var ignoredWorkerVersionsProperty));
            Assert.NotNull(ignoredWorkerVersionsProperty);
            Assert.True(root.TryGetProperty("IsDynamicWorkerResolutionEnabled", out var dynamicWorkerResolutionProperty));
            Assert.True(dynamicWorkerResolutionProperty.GetBoolean());
        }

        [Fact]
        public void Format_WithNullProperties_SerializesSuccessfully()
        {
            var options = new WorkerConfigurationResolverOptions
            {
                WorkersRootDirPath = null
            };

            string json = options.Format();

            Assert.NotNull(json);
            Assert.NotEmpty(json);

            var jsonDocument = JsonDocument.Parse(json);
            Assert.NotNull(jsonDocument);

            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("WorkersRootDirPath", out var workersDirPathProperty));
            Assert.Equal(null, workersDirPathProperty.GetString());
        }

        [Fact]
        public void Configure_WithRealEnvironmentValues_SetsCorrectDefaults()
        {
            // Arrange
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            EnvironmentExtensions.ClearCache();
            var testEnvironment = new TestEnvironment();
            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{RpcWorkerConstants.WorkerProbingPathsSectionName}:0"] = "testPath1",
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{RpcWorkerConstants.WorkerProbingPathsSectionName}:1"] = "testPath2",
                });
            var configuration = configBuilder.Build();
            var mockScriptHostManager = new Mock<IScriptHostManager>();

            testEnvironment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "java");

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java");

            var setup = new WorkerConfigurationResolverOptionsSetup(loggerFactory, configuration, testEnvironment, FileUtility.Instance, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();

            // Act
            setup.Configure(options);

            // Assert
            Assert.Equal(ScriptConstants.LatestPlatformChannelNameUpper, options.ReleaseChannel);
            Assert.False(options.IsPlaceholderModeEnabled);
            Assert.False(options.IsMultiLanguageWorkerEnvironment);
            Assert.Equal("/default/workers", options.WorkersRootDirPath);
            Assert.NotNull(options.WorkerDescriptionOverrides);

            Assert.Equal(2, options.ProbingPaths.Count);
            Assert.True(options.ProbingPaths.Contains("testPath1"));
            Assert.True(options.ProbingPaths.Contains("testPath2"));

            Assert.True(options.WorkersAvailableForResolution.Any());

            var logs = loggerProvider.GetAllLogMessages();
            Assert.Single(logs.Where(l => l.FormattedMessage == "Worker probing paths specified via configuration: testPath1, testPath2."));
        }

        [Fact]
        public void Configure_NoProbingPaths_SetsCorrectValues()
        {
            // Arrange
            var testLoggerFactory = GetTestLoggerFactory();
            var testEnvironment = new TestEnvironment();
            var configuration = new ConfigurationBuilder().Build();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            testEnvironment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "java");

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java");

            var setup = new WorkerConfigurationResolverOptionsSetup(testLoggerFactory, configuration, testEnvironment, FileUtility.Instance, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();
            setup.Configure(options);

            Assert.Equal(0, options.ProbingPaths.Count);
        }

        [Fact]
        public void Configure_WithEnvironmentValues_SetsValues()
        {
            // Arrange
            EnvironmentExtensions.ClearCache();
            var testEnvironment = new TestEnvironment();
            var testLoggerFactory = GetTestLoggerFactory();
            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                });
            var configuration = configBuilder.Build();
            var mockScriptHostManager = new Mock<IScriptHostManager>();

            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "java");
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel, "standard");
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AppKind, "workflowapp");

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node");

            var setup = new WorkerConfigurationResolverOptionsSetup(testLoggerFactory, configuration, testEnvironment, FileUtility.Instance, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();

            // Act
            setup.Configure(options);

            // Assert
            Assert.Equal("java", options.WorkerRuntime);
            Assert.Equal("standard", options.ReleaseChannel);
            Assert.False(options.IsPlaceholderModeEnabled);
            Assert.True(options.IsMultiLanguageWorkerEnvironment);
            Assert.Equal("/default/workers", options.WorkersRootDirPath);
            Assert.NotNull(options.WorkerDescriptionOverrides);

            Assert.NotNull(options.ProbingPaths);
            Assert.False(options.ProbingPaths.Any());

            Assert.True(options.WorkersAvailableForResolution.Count == 2);
            Assert.True(options.WorkersAvailableForResolution.Contains("java"));
            Assert.True(options.WorkersAvailableForResolution.Contains("node"));
        }

        [Theory]
        [InlineData(null, "node", true)]
        [InlineData(null, "java|node", true)]
        [InlineData(null, "", false)]
        [InlineData(null, "| ", false)]
        [InlineData(null, null, false)]
        [InlineData(ScriptConstants.FeatureFlagDisableDynamicWorkerResolution, "node", false)]
        [InlineData(ScriptConstants.FeatureFlagDisableDynamicWorkerResolution, "java|node", false)]
        [InlineData(ScriptConstants.FeatureFlagDisableDynamicWorkerResolution, "| ", false)]

        public void IsDynamicWorkerResolutionEnabled_HostingConfigAndFeatureFlags_WorksAsExpected(string featureFlagValue, string hostingConfigSetting, bool expected)
        {
            var config = new ConfigurationBuilder().Build();
            var mockScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, hostingConfigSetting);

            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, featureFlagValue);

            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(config, testEnvironment, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            bool result = optionsMonitor.CurrentValue.IsDynamicWorkerResolutionEnabled;

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("node", "node", null, "0", true)]
        [InlineData("node", "java", null, "0", false)]
        [InlineData("java|node", null, null, "0", true)]
        [InlineData("node", "node", "workflowapp", "0", true)]
        [InlineData("java|node", null, "workflowapp", "0", true)]
        [InlineData("| ", null, "workflowapp", "0", false)]
        [InlineData("java|node", null, null, "1", true)]
        [InlineData("node", "java", null, "1", true)]
        public void IsDynamicWorkerResolutionEnabled_WorksAsExpected(string hostingConfigSetting, string workerRuntime, string multilanguageApp, string placeholdermode, bool expected)
        {
            EnvironmentExtensions.ClearCache();
            var config = new ConfigurationBuilder().Build();
            var mockScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, hostingConfigSetting);

            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AppKind, multilanguageApp);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, workerRuntime);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, placeholdermode);

            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(config, testEnvironment, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            bool result = optionsMonitor.CurrentValue.IsDynamicWorkerResolutionEnabled;

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("| ", 0)]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        [InlineData("java:1.0.0|python:2.0.0|java:1.1.1||node:|dotnet-isolated:abc", 2)]
        public void IgnoredWorkerVersions_WorksAsExpected(string hostingConfigSetting, int expected)
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var config = new ConfigurationBuilder().Build();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable("APP_KIND", "workflowapp");

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.IgnoredWorkerVersions, hostingConfigSetting);
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java");

            var setup = new WorkerConfigurationResolverOptionsSetup(loggerFactory, config, testEnvironment, FileUtility.Instance, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();
            setup.Configure(options);

            var logs = loggerProvider.GetAllLogMessages();

            var ignoreWorkerVersions = options.IgnoredWorkerVersions;
            Assert.NotNull(ignoreWorkerVersions);
            Assert.Equal(ignoreWorkerVersions.Count, expected);

            if (expected != 0)
            {
                ignoreWorkerVersions.TryGetValue("java", out HashSet<Version> javaValue);
                Assert.NotNull(javaValue);
                Assert.Equal(javaValue.Count, 2);
                Assert.Contains(new Version("1.0.0"), javaValue);
                Assert.Contains(new Version("1.1.1"), javaValue);

                ignoreWorkerVersions.TryGetValue("python", out HashSet<Version> pyValue);
                Assert.NotNull(pyValue);
                Assert.Equal(pyValue.Count, 1);
                Assert.Contains(new Version("2.0.0"), pyValue);

                Assert.Single(logs.Where(l => l.FormattedMessage == "Skipping 'node:' due to invalid format for ignored worker version. Expected format is 'WorkerName:Version'."));
                Assert.Single(logs.Where(l => l.FormattedMessage == "Skipping 'dotnet-isolated:abc' due to invalid version format: 'abc' for worker 'dotnet-isolated'."));
            }
        }
    }
}
