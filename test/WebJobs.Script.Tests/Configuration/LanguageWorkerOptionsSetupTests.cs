// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class LanguageWorkerOptionsSetupTests
    {
        private readonly string _probingPath1 = Path.GetFullPath("..\\..\\..\\..\\test\\TestWorkers\\ProbingPaths\\workers\\");
        private readonly string _fallbackPath = Path.GetFullPath("workers");

        [Theory]
        [InlineData("DotNet")]
        [InlineData("dotnet")]
        [InlineData(null)]
        [InlineData("node")]
        public void LanguageWorkerOptions_Expected_ListOfConfigs(string workerRuntime)
        {
            var testEnvironment = new TestEnvironment();
            var testMetricLogger = new TestMetricsLogger();
            var configurationBuilder = new ConfigurationBuilder()
                .Add(new ScriptEnvironmentVariablesConfigurationSource());
            var configuration = configurationBuilder.Build();
            var testProfileManager = new Mock<IWorkerProfileManager>();
            var testScriptHostManager = new Mock<IScriptHostManager>();

            if (!string.IsNullOrEmpty(workerRuntime))
            {
                testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            }
            else
            {
                // The dotnet-isolated worker only runs in placeholder mode. Setting the placeholder environment to 1 for the test.
                testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            }

            testProfileManager.Setup(pm => pm.LoadWorkerDescriptionFromProfiles(It.IsAny<RpcWorkerDescription>(), out It.Ref<RpcWorkerDescription>.IsAny))
                .Callback((RpcWorkerDescription defaultDescription, out RpcWorkerDescription outDescription) =>
                {
                    // dotnet-isolated worker config does not have "DefaultExecutablePath" in the parent level.So, we should set it from a profile.
                    if (defaultDescription.Language == "dotnet-isolated")
                    {
                        outDescription = new RpcWorkerDescription() { DefaultExecutablePath = "testPath", Language = "dotnet-isolated" };
                    }
                    else
                    {
                        // for other workers, we should return the default description as they have the "DefaultExecutablePath" in the parent level.
                        outDescription = defaultDescription;
                    }
                });

            var resolverOptionssetup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, testScriptHostManager.Object);
            var resolverOptions = new WorkerConfigurationResolverOptions();
            resolverOptionssetup.Configure(resolverOptions);

            var factory = new TestOptionsFactory<WorkerConfigurationResolverOptions>(resolverOptions);
            var source = new TestChangeTokenSource<WorkerConfigurationResolverOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<WorkerConfigurationResolverOptions>(factory, changeTokens, factory);

            LanguageWorkerOptionsSetup setup = new LanguageWorkerOptionsSetup(configuration, NullLoggerFactory.Instance, testEnvironment, testMetricLogger, testProfileManager.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(new FunctionsHostingConfigOptions()), optionsMonitor);
            LanguageWorkerOptions options = new LanguageWorkerOptions();

            setup.Configure(options);

            if (string.IsNullOrEmpty(workerRuntime))
            {
                Assert.Equal(5, options.WorkerConfigs.Count);
            }
            else if (workerRuntime.Equals(RpcWorkerConstants.DotNetLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
            {
                Assert.Empty(options.WorkerConfigs);
            }
            else
            {
                Assert.Equal(1, options.WorkerConfigs.Count);
            }
        }

        [Theory]
        [InlineData("java", "java", "LATEST", "2.19.0")]
        [InlineData("java", "java", "STANDARD", "2.18.0")]
        [InlineData("node", "node", "LATEST", "3.10.1")]
        [InlineData("node", "java|node", "STANDARD", "3.10.1")]
        [InlineData("java", "java", "EXTENDED", "2.18.0")]
        [InlineData("node", "java|node", "EXTENDED", "3.10.1")]
        public void LanguageWorkerOptions_EnabledWorkerResolution_Expected_ListOfConfigs(string workerRuntime, string hostingOptionsSetting, string releaseChannel, string expectedVersion)
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testEnvironment = new TestEnvironment();
            var testMetricLogger = new TestMetricsLogger();
            var configurationBuilder = new ConfigurationBuilder()
                .Add(new ScriptEnvironmentVariablesConfigurationSource());
            var configuration = configurationBuilder.Build();
            var testProfileManager = new Mock<IWorkerProfileManager>();
            var testScriptHostManager = new Mock<IScriptHostManager>();
            string probingPathValue = string.Join(';', _probingPath1, string.Empty, "path-not-exists");

            testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel, releaseChannel);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.WorkerProbingPaths, probingPathValue);

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, hostingOptionsSetting);

            var resolverOptionssetup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, testScriptHostManager.Object);
            var resolverOptions = new WorkerConfigurationResolverOptions();
            resolverOptionssetup.Configure(resolverOptions);

            var factory = new TestOptionsFactory<WorkerConfigurationResolverOptions>(resolverOptions);
            var source = new TestChangeTokenSource<WorkerConfigurationResolverOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<WorkerConfigurationResolverOptions>(factory, changeTokens, factory);

            LanguageWorkerOptionsSetup setup = new LanguageWorkerOptionsSetup(configuration, loggerFactory, testEnvironment, testMetricLogger, testProfileManager.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions), optionsMonitor);
            LanguageWorkerOptions options = new LanguageWorkerOptions();

            setup.Configure(options);

            Assert.Equal(1, options.WorkerConfigs.Count);
            Assert.True(options.WorkerConfigs.First().Arguments.WorkerPath.Contains(expectedVersion));

            var logs = loggerProvider.GetAllLogMessages();

            string path = Path.Combine(_probingPath1, workerRuntime, expectedVersion);
            string expectedLog = $"Added WorkerConfig for language: {workerRuntime} with worker path: {path}";
            Assert.True(logs.Any(l => l.FormattedMessage.Contains(expectedLog)));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Workers probing paths set to:")));
        }

        [Theory]
        [InlineData("java", "java", "LATEST")]
        [InlineData("java", "java", "STANDARD")]
        [InlineData("node", "node", "LATEST")]
        [InlineData("node", "java|node", "STANDARD")]
        public void LanguageWorkerOptions_FallbackPath_Expected_ListOfConfigs(string workerRuntime, string hostingOptionsSetting, string releaseChannel)
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testEnvironment = new TestEnvironment();
            var testMetricLogger = new TestMetricsLogger();
            var configurationBuilder = new ConfigurationBuilder()
                .Add(new ScriptEnvironmentVariablesConfigurationSource());
            var configuration = configurationBuilder.Build();
            var testProfileManager = new Mock<IWorkerProfileManager>();
            var testScriptHostManager = new Mock<IScriptHostManager>();
            string probingPathValue = null;

            testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel, releaseChannel);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.WorkerProbingPaths, probingPathValue);

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, hostingOptionsSetting);

            var resolverOptionssetup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, testScriptHostManager.Object);
            var resolverOptions = new WorkerConfigurationResolverOptions();
            resolverOptionssetup.Configure(resolverOptions);

            var factory = new TestOptionsFactory<WorkerConfigurationResolverOptions>(resolverOptions);
            var source = new TestChangeTokenSource<WorkerConfigurationResolverOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<WorkerConfigurationResolverOptions>(factory, changeTokens, factory);

            LanguageWorkerOptionsSetup setup = new LanguageWorkerOptionsSetup(configuration, loggerFactory, testEnvironment, testMetricLogger, testProfileManager.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions), optionsMonitor);
            LanguageWorkerOptions options = new LanguageWorkerOptions();

            setup.Configure(options);

            Assert.Equal(1, options.WorkerConfigs.Count);

            var logs = loggerProvider.GetAllLogMessages();

            string path = Path.Combine(_fallbackPath, workerRuntime);
            string expectedLog = $"Added WorkerConfig for language: {workerRuntime} with worker path: {path}";
            Assert.True(logs.Any(l => l.FormattedMessage.Contains(expectedLog)));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Workers probing paths set to:")));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Searching for worker configs in the fallback directory")));
        }

        [Theory]
        [InlineData("java", null, "LATEST")]
        [InlineData("java", "", "STANDARD")]
        [InlineData("node", "  ", "LATEST")]
        public void LanguageWorkerOptions_NullHostingConfig_FeatureDisabled_ListOfConfigs(string workerRuntime, string hostingOptionsSetting, string releaseChannel)
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testEnvironment = new TestEnvironment();
            var testMetricLogger = new TestMetricsLogger();
            var configurationBuilder = new ConfigurationBuilder()
                .Add(new ScriptEnvironmentVariablesConfigurationSource());
            var configuration = configurationBuilder.Build();
            var testProfileManager = new Mock<IWorkerProfileManager>();
            var testScriptHostManager = new Mock<IScriptHostManager>();
            string probingPathValue = string.Join(';', _probingPath1, string.Empty, "path-not-exists");

            testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel, releaseChannel);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.WorkerProbingPaths, probingPathValue);

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, hostingOptionsSetting);

            var resolverOptionssetup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, testScriptHostManager.Object);
            var resolverOptions = new WorkerConfigurationResolverOptions();
            resolverOptionssetup.Configure(resolverOptions);

            var factory = new TestOptionsFactory<WorkerConfigurationResolverOptions>(resolverOptions);
            var source = new TestChangeTokenSource<WorkerConfigurationResolverOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<WorkerConfigurationResolverOptions>(factory, changeTokens, factory);

            LanguageWorkerOptionsSetup setup = new LanguageWorkerOptionsSetup(configuration, loggerFactory, testEnvironment, testMetricLogger, testProfileManager.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions), optionsMonitor);
            LanguageWorkerOptions options = new LanguageWorkerOptions();

            setup.Configure(options);

            Assert.Equal(1, options.WorkerConfigs.Count);

            var logs = loggerProvider.GetAllLogMessages();

            string path = Path.Combine(_fallbackPath, workerRuntime);
            string expectedLog = $"Added WorkerConfig for language: {workerRuntime} with worker path: {path}";
            Assert.True(logs.Any(l => l.FormattedMessage.Contains(expectedLog)));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Workers Directory set to:")));
        }

        [Theory]
        [InlineData("java", "LATEST")]
        [InlineData("java", "STANDARD")]
        [InlineData("node", "LATEST")]
        [InlineData("node", "STANDARD")]
        public void LanguageWorkerOptions_DisabledWorkerResolution_Expected_ListOfConfigs(string workerRuntime, string releaseChannel)
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testEnvironment = new TestEnvironment();
            var testMetricLogger = new TestMetricsLogger();
            var configurationBuilder = new ConfigurationBuilder()
                .Add(new ScriptEnvironmentVariablesConfigurationSource());
            var configuration = configurationBuilder.Build();
            var testProfileManager = new Mock<IWorkerProfileManager>();
            var testScriptHostManager = new Mock<IScriptHostManager>();
            string probingPathValue = null;

            testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel, releaseChannel);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.WorkerProbingPaths, probingPathValue);

            var hostingOptions = new FunctionsHostingConfigOptions();

            var resolverOptionssetup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, testScriptHostManager.Object);
            var resolverOptions = new WorkerConfigurationResolverOptions();
            resolverOptionssetup.Configure(resolverOptions);

            var factory = new TestOptionsFactory<WorkerConfigurationResolverOptions>(resolverOptions);
            var source = new TestChangeTokenSource<WorkerConfigurationResolverOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<WorkerConfigurationResolverOptions>(factory, changeTokens, factory);

            LanguageWorkerOptionsSetup setup = new LanguageWorkerOptionsSetup(configuration, loggerFactory, testEnvironment, testMetricLogger, testProfileManager.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions), optionsMonitor);
            LanguageWorkerOptions options = new LanguageWorkerOptions();

            setup.Configure(options);

            Assert.Equal(1, options.WorkerConfigs.Count);

            var logs = loggerProvider.GetAllLogMessages();

            string path = Path.Combine(_fallbackPath, workerRuntime);
            string expectedLog = $"Added WorkerConfig for language: {workerRuntime} with worker path: {path}";
            Assert.True(logs.Any(l => l.FormattedMessage.Contains(expectedLog)));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Workers Directory set to:")));
        }

        [Theory]
        [InlineData("java")]
        [InlineData("node")]
        public void LanguageWorkerOptions_FallbackPath_NullHostingConfig(string workerRuntime)
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testEnvironment = new TestEnvironment();
            var testMetricLogger = new TestMetricsLogger();
            var configurationBuilder = new ConfigurationBuilder()
                .Add(new ScriptEnvironmentVariablesConfigurationSource());
            var configuration = configurationBuilder.Build();
            var testProfileManager = new Mock<IWorkerProfileManager>();
            var testScriptHostManager = new Mock<IScriptHostManager>();
            string probingPathValue = null;

            testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.WorkerProbingPaths, probingPathValue);

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, workerRuntime);

            var resolverOptionssetup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, testScriptHostManager.Object);
            var resolverOptions = new WorkerConfigurationResolverOptions();
            resolverOptionssetup.Configure(resolverOptions);

            var factory = new TestOptionsFactory<WorkerConfigurationResolverOptions>(resolverOptions);
            var source = new TestChangeTokenSource<WorkerConfigurationResolverOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<WorkerConfigurationResolverOptions>(factory, changeTokens, factory);

            LanguageWorkerOptionsSetup setup = new LanguageWorkerOptionsSetup(configuration, loggerFactory, testEnvironment, testMetricLogger, testProfileManager.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions), optionsMonitor);
            LanguageWorkerOptions options = new LanguageWorkerOptions();

            setup.Configure(options);

            Assert.Equal(1, options.WorkerConfigs.Count);

            var logs = loggerProvider.GetAllLogMessages();

            string path = Path.Combine(_fallbackPath, workerRuntime);
            string expectedLog = $"Added WorkerConfig for language: {workerRuntime} with worker path: {path}";
            Assert.True(logs.Any(l => l.FormattedMessage.Contains(expectedLog)));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Workers probing paths set to:")));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Searching for worker configs in the fallback directory")));
        }
    }
}