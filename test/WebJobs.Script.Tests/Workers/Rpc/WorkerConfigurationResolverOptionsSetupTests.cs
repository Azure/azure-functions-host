// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class WorkerConfigurationResolverOptionsSetupTests
    {
        [Fact]
        public void Configure_WithEnvironmentValues_SetsCorrectValues()
        {
            var loggerFactory = WorkerConfigurationResolverTestsHelper.GetTestLoggerFactory();
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
        public void Configure_WithEnvironmentValues_UpdatedConfiguration_SetsCorrectValues()
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
            Assert.Single(logs.Where(l => l.FormattedMessage == "Found configuration section 'languageWorkers:workersDirectory' in 'latestConfiguration'."));
        }

        [Fact]
        public void Configure_WithEnvironmentValues_WithConfiguration_SetsCorrectValues()
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
            Assert.Single(logs.Where(l => l.FormattedMessage == "Found configuration section 'languageWorkers:workersDirectory' in '_configuration'."));
        }

        [Fact]
        public void Configure_WithNullConfigValues_SetsCorrectValues()
        {
            var testLoggerFactory = WorkerConfigurationResolverTestsHelper.GetTestLoggerFactory();
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
            var testLoggerFactory = WorkerConfigurationResolverTestsHelper.GetTestLoggerFactory();
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
                WorkersRootDirPath = "/test/workers"
            };

            string json = options.Format();

            Assert.NotNull(json);
            Assert.NotEmpty(json);

            var jsonDocument = JsonDocument.Parse(json);
            Assert.NotNull(jsonDocument);

            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("WorkersRootDirPath", out var workersDirPathProperty));
            Assert.Equal("/test/workers", workersDirPathProperty.GetString());
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
            var testLoggerFactory = WorkerConfigurationResolverTestsHelper.GetTestLoggerFactory();
            var testEnvironment = new TestEnvironment();
            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{RpcWorkerConstants.WorkerProbingPathsSectionName}:0"] = "testPath1",
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{RpcWorkerConstants.WorkerProbingPathsSectionName}:1"] = "testPath2",
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{RpcWorkerConstants.WorkerProbingPathsSectionName}:2"] = " ",
                });
            var configuration = configBuilder.Build();
            var mockScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();

            var setup = new WorkerConfigurationResolverOptionsSetup(testLoggerFactory, configuration, testEnvironment, FileUtility.Instance, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();

            // Act
            setup.Configure(options);

            // Assert
            Assert.Empty(options.WorkerRuntime);
            Assert.Equal(ScriptConstants.LatestPlatformChannelNameUpper, options.ReleaseChannel);
            Assert.False(options.IsPlaceholderModeEnabled);
            Assert.False(options.IsMultiLanguageWorkerEnvironment);
            Assert.Equal("/default/workers", options.WorkersRootDirPath);
            Assert.NotNull(options.LanguageWorkersSettings);

            Assert.Equal(2, options.ProbingPaths.Count);
            Assert.True(options.ProbingPaths.Contains("testPath1"));
            Assert.True(options.ProbingPaths.Contains("testPath2"));

            Assert.False(options.WorkersAvailableForResolution.Any());
        }

        [Fact]
        public void Configure_WithEnvironmentValues_SetsValues()
        {
            // Arrange
            var testEnvironment = new TestEnvironment();
            var testLoggerFactory = WorkerConfigurationResolverTestsHelper.GetTestLoggerFactory();
            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                });
            var configuration = configBuilder.Build();
            var mockScriptHostManager = new Mock<IScriptHostManager>();

            testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, "java");
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
            Assert.NotNull(options.LanguageWorkersSettings);

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
            var mockConfiguration = new Mock<IConfiguration>();
            var mockScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, hostingConfigSetting);

            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, featureFlagValue);

            var optionsMonitor = WorkerConfigurationResolverTestsHelper.GetTestWorkerConfigurationResolverOptions(mockConfiguration.Object, testEnvironment, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            bool result = optionsMonitor.CurrentValue.IsDynamicWorkerResolutionEnabled;

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("node", "node", null, true)]
        [InlineData("node", "java", null, false)]
        [InlineData("java|node", null, null, true)]
        [InlineData("node", "node", "workflowapp", true)]
        [InlineData("java|node", null, "workflowapp", true)]
        [InlineData("| ", null, "workflowapp", false)]
        public void IsDynamicWorkerResolutionEnabled_WorkerRuntimeAndMultiLanguage_WorksAsExpected(string hostingConfigSetting, string workerRuntime, string multilanguageApp, bool expected)
        {
            var mockConfiguration = new Mock<IConfiguration>();
            var mockScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, hostingConfigSetting);

            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AppKind, multilanguageApp);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, workerRuntime);

            var optionsMonitor = WorkerConfigurationResolverTestsHelper.GetTestWorkerConfigurationResolverOptions(mockConfiguration.Object, testEnvironment, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            bool result = optionsMonitor.CurrentValue.IsDynamicWorkerResolutionEnabled;

            Assert.Equal(expected, result);
        }
    }
}
