// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
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
    public class DynamicWorkerConfigurationResolverTests
    {
        private readonly string _probingPath1 = Path.GetFullPath("..\\..\\..\\..\\test\\TestWorkers\\ProbingPaths\\functionsworkers\\");
        private readonly string _fallbackPath = Path.GetFullPath("workers");

        public DynamicWorkerConfigurationResolverTests()
        {
            EnvironmentExtensions.ClearCache();
        }

        [Theory]
        [InlineData("LATEST", "java\\2.19.0", "node\\3.10.1", "powershell", "dotnet-isolated", "python")]
        [InlineData("STANDARD", "java\\2.18.0", "node\\3.10.1", "powershell", "dotnet-isolated", "python")]
        [InlineData("EXTENDED", "java\\2.18.0", "node\\3.10.1", "powershell", "dotnet-isolated", "python")]
        [InlineData("laTest", "java\\2.19.0", "node\\3.10.1", "powershell", "dotnet-isolated", "python")]
        [InlineData("abc", "java\\2.19.0", "node\\3.10.1", "powershell", "dotnet-isolated", "python")]
        [InlineData("Standard", "java\\2.18.0", "node\\3.10.1", "powershell", "dotnet-isolated", "python")]
        public void GetWorkerConfigs_MultiLanguageWorker_ReturnsExpectedConfigs(string releaseChannel, string java, string node, string powershell, string dotnetIsolated, string python)
        {
            // Arrange
            var probingPaths = new List<string>() { _probingPath1, string.Empty, "path-not-exists" };
            var fileSystem = new FileSystem();

            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var testMetricLogger = new TestMetricsLogger();

            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns("Windows");

            var config = WorkerConfigurationResolverTestsHelper.GetConfigurationWithProbingPaths(probingPaths);

            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            var workerProfileManager = new WorkerProfileManager(workerProfileLogger, mockEnvironment.Object);
            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node");
            var optionsMonitor = WorkerConfigurationResolverTestsHelper.GetTestWorkerConfigurationResolverOptions(config, mockEnvironment.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(loggerFactory, testMetricLogger, fileSystem, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor);

            var result = workerConfigurationResolver.GetWorkerConfigs();

            // Assert
            Assert.Equal(result.Count, 5);
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_probingPath1, java))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_probingPath1, node))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, powershell))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, dotnetIsolated))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, python))));

            var logs = loggerProvider.GetAllLogMessages();
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Worker probing paths set to:")));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Worker configuration at ") && l.FormattedMessage.Contains("\\ProbingPaths\\functionsworkers\\java\\2.19.0\\worker.config.json' specifies host requirements [].")));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Worker configuration at ") && l.FormattedMessage.Contains("\\ProbingPaths\\functionsworkers\\node\\3.10.1\\worker.config.json' specifies host requirements [].")));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Worker probing path directory does not exist: path-not-exists.")));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Searching for worker configs in the fallback directory")));
        }

        [Theory]
        [InlineData("LATEST", "java", "node", "powershell", "dotnet-isolated", "python")]
        [InlineData("STANDARD", "java", "node", "powershell", "dotnet-isolated", "python")]
        public void GetWorkerConfigs_MultiLanguageWorker_MalformedProbingPath_ReturnsExpectedConfigs(string releaseChannel, string java, string node, string powershell, string dotnetIsolated, string python)
        {
            // Arrange
            var probingPaths = new List<string>() { _fallbackPath };
            var fileSystem = new FileSystem();

            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns("Windows");

            var config = WorkerConfigurationResolverTestsHelper.GetConfigurationWithProbingPaths(probingPaths);

            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            var workerProfileManager = new WorkerProfileManager(workerProfileLogger, mockEnvironment.Object);
            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node");
            var optionsMonitor = WorkerConfigurationResolverTestsHelper.GetTestWorkerConfigurationResolverOptions(config, mockEnvironment.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var testMetricLogger = new TestMetricsLogger();

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(loggerFactory, testMetricLogger, fileSystem, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor);

            var result = workerConfigurationResolver.GetWorkerConfigs();

            // Assert
            Assert.Equal(result.Count, 5);
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, java))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, node))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, powershell))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, dotnetIsolated))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, python))));

            var logs = loggerProvider.GetAllLogMessages();
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Worker probing paths set to:")));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Failed to parse worker version")));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Searching for worker configs in the fallback directory")));
        }

        [Theory]
        [InlineData(null, "LATEST")]
        [InlineData(null, "STANDARD")]
        [InlineData("Empty", "LATEST")]
        [InlineData("Empty", "abc")]
        public void GetWorkerConfigs_MultiLanguageWorker_NullOREmptyProbingPath_ReturnsExpectedConfigs(string probingPathValue, string releaseChannel)
        {
            // Arrange
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns("Windows");

            List<string> probingPaths = null;

            if (probingPathValue == "Empty")
            {
                probingPaths = new List<string>();
            }

            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            var workerProfileManager = new WorkerProfileManager(workerProfileLogger, mockEnvironment.Object);
            var config = WorkerConfigurationResolverTestsHelper.GetConfigurationWithProbingPaths(probingPaths);
            var fileSystem = new FileSystem();

            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node|powershell");
            var optionsMonitor = WorkerConfigurationResolverTestsHelper.GetTestWorkerConfigurationResolverOptions(config, mockEnvironment.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var testMetricLogger = new TestMetricsLogger();

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(loggerFactory, testMetricLogger, fileSystem, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor);

            var result = workerConfigurationResolver.GetWorkerConfigs();

            // Assert
            Assert.Equal(result.Count, 5);
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, "java"))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, "node"))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, "powershell"))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, "dotnet-isolated"))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, "python"))));
        }

        [Theory]
        [InlineData(null, "LATEST", "java")]
        [InlineData(null, "STANDARD", "java")]
        [InlineData("Empty", "LATEST", "java")]
        [InlineData("Empty", "STANDARD", "java")]
        [InlineData(null, "STANDARD", "node")]
        [InlineData("Empty", "LATEST", "node")]
        [InlineData(null, "STANDARD", "powershell")]
        [InlineData("Empty", "LATEST", "powershell")]
        public void GetWorkerConfigs_NullOREmptyProbingPath_ReturnsExpectedConfigs(string probingPathValue, string releaseChannel, string languageWorker)
        {
            // Arrange
            var mockEnv = new Mock<IEnvironment>();
            mockEnv.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns(languageWorker);
            mockEnv.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
          //  mockEnv.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnv.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns("Windows");

            List<string> probingPaths = null;

            if (probingPathValue == "Empty")
            {
                probingPaths = new List<string>();
            }

            var config = WorkerConfigurationResolverTestsHelper.GetConfigurationWithProbingPaths(probingPaths);

            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            var workerProfileManager = new WorkerProfileManager(workerProfileLogger, mockEnv.Object);
            var mockConfig = new Mock<IConfiguration>();

            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node|powershell");
            var optionsMonitor = WorkerConfigurationResolverTestsHelper.GetTestWorkerConfigurationResolverOptions(config, mockEnv.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var testMetricLogger = new TestMetricsLogger();

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(loggerFactory, testMetricLogger, FileUtility.Instance, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor);

            var result = workerConfigurationResolver.GetWorkerConfigs();

            // Assert
            Assert.Equal(result.Count, 1);
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, languageWorker))));
        }

        [Theory]
        [InlineData("LATEST", "java:2.19.0", "java\\2.18.0", "node\\3.10.1", "powershell", "dotnet-isolated", "python")]
        [InlineData("LATEST", "java:2.19.0|python:4.1.0", "java\\2.18.0", "node\\3.10.1", "powershell", "dotnet-isolated", "python")]
        [InlineData("LATEST", "java:xyz|node:a.b.c", "java\\2.19.0", "node\\3.10.1", "powershell", "dotnet-isolated", "python")]
        public void GetWorkerConfigs_MultiLang_IgnoredVersion_ReturnsExpectedConfigs(string releaseChannel, string setting, string java, string node, string powershell, string dotnetIsolated, string python)
        {
            // Arrange
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns("Windows");

            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            var workerProfileManager = new WorkerProfileManager(workerProfileLogger, mockEnvironment.Object);

            var probingPaths = new List<string>() { _probingPath1, string.Empty, "path-not-exists" };
            var config = WorkerConfigurationResolverTestsHelper.GetConfigurationWithProbingPaths(probingPaths);

            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node|powershell");
            hostingOptions.Features.Add(RpcWorkerConstants.IgnoredWorkerVersions, setting);
            var optionsMonitor = WorkerConfigurationResolverTestsHelper.GetTestWorkerConfigurationResolverOptions(config, mockEnvironment.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var testMetricLogger = new TestMetricsLogger();

            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(loggerFactory, testMetricLogger, FileUtility.Instance, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor);

            var result = workerConfigurationResolver.GetWorkerConfigs();

            // Assert
            Assert.Equal(result.Count, 5);
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_probingPath1, java))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_probingPath1, node))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, powershell))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, dotnetIsolated))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, python))));
        }

        [Theory]
        [InlineData("java:2.18.0|java:2.19.0", "java")]
        public void GetWorkerConfigs_IgnoredVersion_ReturnsExpectedConfigs(string setting, string workerRuntime)
        {
            // Arrange
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns(workerRuntime);

            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            var workerProfileManager = new WorkerProfileManager(workerProfileLogger, mockEnvironment.Object);

            var probingPaths = new List<string>() { _probingPath1, string.Empty, "path-not-exists" };
            var config = WorkerConfigurationResolverTestsHelper.GetConfigurationWithProbingPaths(probingPaths);

            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node|powershell");
            hostingOptions.Features.Add(RpcWorkerConstants.IgnoredWorkerVersions, setting);
            var optionsMonitor = WorkerConfigurationResolverTestsHelper.GetTestWorkerConfigurationResolverOptions(config, mockEnvironment.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var testMetricLogger = new TestMetricsLogger();

            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(loggerFactory, testMetricLogger, FileUtility.Instance, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor);

            var result = workerConfigurationResolver.GetWorkerConfigs();

            // Assert
            Assert.Equal(result.Count, 1);
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_fallbackPath, workerRuntime))));
        }
    }
}