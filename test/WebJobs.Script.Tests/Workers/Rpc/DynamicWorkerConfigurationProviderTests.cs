// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Tests.WorkerConfigurationResolverTestsHelper;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class DynamicWorkerConfigurationProviderTests
    {
        private readonly string _probingPath = Path.GetFullPath("..\\..\\..\\..\\test\\TestWorkers\\ProbingPaths\\functionsworkers\\");
        private readonly string _fallbackPath = Path.GetFullPath("workers");

        public DynamicWorkerConfigurationProviderTests()
        {
            EnvironmentExtensions.ClearCache();
        }

        [Theory]
        [InlineData("LATEST", "1", "java\\2.19.0", "node\\3.10.1")]
        [InlineData("STANDARD", "0", "java\\2.18.0", "node\\3.10.1")]
        [InlineData("EXTENDED", "1", "java\\2.18.0", "node\\3.10.1")]
        [InlineData("laTest", "0", "java\\2.19.0", "node\\3.10.1")]
        [InlineData("abc", "1", "java\\2.19.0", "node\\3.10.1")]
        [InlineData("Standard", "1", "java\\2.18.0", "node\\3.10.1")]
        public void GetWorkerConfigs_MultiLanguageWorker_ReturnsExpectedConfigs(string releaseChannel, string placeholderMode, string java, string node)
        {
            // Arrange
            var probingPaths = new List<string>() { _probingPath, string.Empty, "path-not-exists" };
            var fileSystem = new FileSystem();
            var testMetricLogger = new TestMetricsLogger();

            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns(placeholderMode);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns("Windows");

            var config = GetConfigurationWithProbingPaths(probingPaths);

            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            var workerProfileManager = new WorkerProfileManager(workerProfileLogger, mockEnvironment.Object);
            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node");
            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(
                config,
                workerRuntime: null,
                mockEnvironment.Object,
                testScriptHostManager.Object,
                new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var logger = new TestLogger<DynamicWorkerConfigurationProvider>();

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationProvider(logger, testMetricLogger, fileSystem, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor);
            var result = new Dictionary<string, RpcWorkerConfig>();

            workerConfigurationResolver.PopulateWorkerConfigs(result);

            // Assert
            Assert.Equal(result.Count, 2);
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_probingPath, java))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_probingPath, node))));

            var logs = logger.GetLogMessages();
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Worker probing paths set to:")));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Worker configuration at ") && l.FormattedMessage.Contains("\\ProbingPaths\\functionsworkers\\java\\2.19.0' specifies host requirements [].")));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Worker configuration at ") && l.FormattedMessage.Contains("\\ProbingPaths\\functionsworkers\\node\\3.10.1' specifies host requirements [].")));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Worker probing path directory does not exist: path-not-exists.")));
        }

        [Theory]
        [InlineData("LATEST")]
        [InlineData("STANDARD")]
        public void GetWorkerConfigs_MultiLanguageWorker_MalformedProbingPath_ReturnsExpectedConfigs(string releaseChannel)
        {
            // Arrange
            var probingPaths = new List<string>() { _fallbackPath };
            var fileSystem = new FileSystem();

            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns("Windows");

            var config = GetConfigurationWithProbingPaths(probingPaths);

            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            var workerProfileManager = new WorkerProfileManager(workerProfileLogger, mockEnvironment.Object);
            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node");
            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(
                config,
                workerRuntime: null,
                mockEnvironment.Object,
                testScriptHostManager.Object,
                new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var testMetricLogger = new TestMetricsLogger();
            var logger = new TestLogger<DynamicWorkerConfigurationProvider>();

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationProvider(logger, testMetricLogger, fileSystem, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor);
            var result = new Dictionary<string, RpcWorkerConfig>();

            workerConfigurationResolver.PopulateWorkerConfigs(result);

            // Assert
            Assert.Equal(result.Count, 0);

            var logs = logger.GetLogMessages();
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Worker probing paths set to:")));
            Assert.True(logs.Any(l => l.FormattedMessage.Contains("Failed to parse worker version")));
        }

        [Theory]
        [InlineData(null, "LATEST", null)]
        [InlineData(null, "STANDARD", null)]
        [InlineData("Empty", "LATEST", null)]
        [InlineData("Empty", "abc", null)]
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
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns("Windows");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)languageWorker);

            if (languageWorker is null)
            {
                mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            }

            List<string> probingPaths = null;

            if (probingPathValue == "Empty")
            {
                probingPaths = new List<string>();
            }

            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            var workerProfileManager = new WorkerProfileManager(workerProfileLogger, mockEnvironment.Object);
            var config = GetConfigurationWithProbingPaths(probingPaths);
            var fileSystem = new FileSystem();
            var logger = new TestLogger<DynamicWorkerConfigurationProvider>();
            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node|powershell");
            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(
                config,
                languageWorker,
                mockEnvironment.Object,
                testScriptHostManager.Object,
                new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var testMetricLogger = new TestMetricsLogger();

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationProvider(logger, testMetricLogger, fileSystem, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor);
            var result = new Dictionary<string, RpcWorkerConfig>();

            workerConfigurationResolver.PopulateWorkerConfigs(result);

            Assert.Equal(result.Count, 0);
        }

        [Theory]
        [InlineData("LATEST", "java:2.19.0", "java\\2.18.0", "node\\3.10.1")]
        [InlineData("LATEST", "java:2.19.0|python:4.1.0", "java\\2.18.0", "node\\3.10.1")]
        [InlineData("LATEST", "java:xyz|node:a.b.c", "java\\2.19.0", "node\\3.10.1")]
        public void GetWorkerConfigs_MultiLang_IgnoredVersion_ReturnsExpectedConfigs(string releaseChannel, string setting, string java, string node)
        {
            // Arrange
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns("1");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns("Windows");

            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            var workerProfileManager = new WorkerProfileManager(workerProfileLogger, mockEnvironment.Object);

            var probingPaths = new List<string>() { _probingPath, string.Empty, "path-not-exists" };
            var config = GetConfigurationWithProbingPaths(probingPaths);

            var testScriptHostManager = new Mock<IScriptHostManager>();
            var logger = new TestLogger<DynamicWorkerConfigurationProvider>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node|powershell");
            hostingOptions.Features.Add(RpcWorkerConstants.IgnoredWorkerVersions, setting);
            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(
                config,
                workerRuntime: null,
                mockEnvironment.Object,
                testScriptHostManager.Object,
                new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var testMetricLogger = new TestMetricsLogger();

            var workerConfigurationResolver = new DynamicWorkerConfigurationProvider(logger, testMetricLogger, FileUtility.Instance, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor);
            var result = new Dictionary<string, RpcWorkerConfig>();

            workerConfigurationResolver.PopulateWorkerConfigs(result);

            // Assert
            Assert.Equal(result.Count, 2);
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_probingPath, java))));
            Assert.True(result.Any(r => r.Value.Description.DefaultWorkerPath.Contains(Path.Combine(_probingPath, node))));
        }

        [Theory]
        [InlineData("java:2.18.0|java:2.19.0", "java")]
        [InlineData("java:2.18.0|node:3.10.1", "node")]
        public void GetWorkerConfigs_SingleWorker_IgnoredVersion_ReturnsExpectedConfigs(string setting, string workerRuntime)
        {
            // Arrange
            var mockEnvironment = new Mock<IEnvironment>();
            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            var workerProfileManager = new WorkerProfileManager(workerProfileLogger, mockEnvironment.Object);

            var probingPaths = new List<string>() { _probingPath, string.Empty, "path-not-exists" };
            var config = GetConfigurationWithProbingPaths(probingPaths);

            var logger = new TestLogger<DynamicWorkerConfigurationProvider>();
            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node|powershell");
            hostingOptions.Features.Add(RpcWorkerConstants.IgnoredWorkerVersions, setting);
            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(
                config,
                workerRuntime,
                mockEnvironment.Object,
                testScriptHostManager.Object,
                new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var testMetricLogger = new TestMetricsLogger();

            var workerConfigurationResolver = new DynamicWorkerConfigurationProvider(logger, testMetricLogger, FileUtility.Instance, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor);
            var result = new Dictionary<string, RpcWorkerConfig>();

            workerConfigurationResolver.PopulateWorkerConfigs(result);

            // Assert
            Assert.Equal(result.Count, 0);
        }
    }
}