// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class WorkerConfigurationResolverTests
    {
        private readonly Mock<IWorkerProfileManager> _mockProfileManager;
        private readonly Mock<IConfiguration> _mockConfig;
        private readonly Mock<ILogger> _mockLogger;
        private readonly string _probingPath1 = Path.GetFullPath("..\\..\\..\\..\\test\\TestWorkers\\ProbingPaths\\workers\\");
        private readonly string _fallbackPath = Path.GetFullPath("workers");
        private List<string> _probingPaths;

        public WorkerConfigurationResolverTests()
        {
            _mockProfileManager = new Mock<IWorkerProfileManager>();
            _mockConfig = new Mock<IConfiguration>();
            _mockLogger = new Mock<ILogger>();

            _probingPaths = new List<string> { _probingPath1, string.Empty, null, "path-not-exists" };
        }

        [Theory]
        [InlineData("LATEST", "java\\2.19.0", "node\\3.10.1", "powershell\\7.4", "dotnet-isolated", "python")]
        [InlineData("STANDARD", "java\\2.18.0", "node\\3.10.1", "powershell\\7.4", "dotnet-isolated", "python")]
        [InlineData("EXTENDED", "java\\2.18.0", "node\\3.10.1", "powershell\\7.4", "dotnet-isolated", "python")]
        [InlineData("laTest", "java\\2.19.0", "node\\3.10.1", "powershell\\7.4", "dotnet-isolated", "python")]
        [InlineData("abc", "java\\2.19.0", "node\\3.10.1", "powershell\\7.4", "dotnet-isolated", "python")]
        [InlineData("Standard", "java\\2.18.0", "node\\3.10.1", "powershell\\7.4", "dotnet-isolated", "python")]
        public void GetWorkerConfigs_MultiLanguageWorker_ReturnsExpectedConfigs(string releaseChannel, string java, string node, string powershell, string dotnetIsolated, string python)
        {
            // Arrange
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);

            var testScriptHostManager = new Mock<IScriptHostManager>();

            var resolverOptionssetup = new WorkerConfigurationResolverOptionsSetup(_mockConfig.Object, mockEnvironment.Object, testScriptHostManager.Object);
            var resolverOptions = new WorkerConfigurationResolverOptions();
            resolverOptionssetup.Configure(resolverOptions);

            var factory = new TestOptionsFactory<WorkerConfigurationResolverOptions>(resolverOptions);
            var source = new TestChangeTokenSource<WorkerConfigurationResolverOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<WorkerConfigurationResolverOptions>(factory, changeTokens, factory);

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(_mockLogger.Object, FileUtility.Instance, _mockProfileManager.Object, new HashSet<string>() { "java", "node", "powershell" }, _probingPaths, optionsMonitor);

            var result = workerConfigurationResolver.GetWorkerConfigPaths();

            // Assert
            Assert.Equal(result.Count, 5);
            Assert.True(result.Any(r => r.Contains(Path.Combine(_probingPath1, java))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_probingPath1, node))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_probingPath1, powershell))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, dotnetIsolated))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, python))));
        }

        [Theory]
        [InlineData(null, "LATEST")]
        [InlineData(null, "STANDARD")]
        [InlineData("Empty", "LATEST")]
        [InlineData("Empty", "abc")]
        public void GetWorkerConfigs_MultiLanguageWorker_NullOREmptyProbingPath_ReturnsExpectedConfigs(string probingPathValue, string releaseChannel)
        {
            // Arrange
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);

            List<string> probingPaths = null;

            if (probingPathValue == "Empty")
            {
                probingPaths = new List<string>();
            }

            var testScriptHostManager = new Mock<IScriptHostManager>();
            var resolverOptionssetup = new WorkerConfigurationResolverOptionsSetup(_mockConfig.Object, mockEnvironment.Object, testScriptHostManager.Object);
            var resolverOptions = new WorkerConfigurationResolverOptions();
            resolverOptionssetup.Configure(resolverOptions);

            var factory = new TestOptionsFactory<WorkerConfigurationResolverOptions>(resolverOptions);
            var source = new TestChangeTokenSource<WorkerConfigurationResolverOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<WorkerConfigurationResolverOptions>(factory, changeTokens, factory);

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(_mockLogger.Object, FileUtility.Instance, _mockProfileManager.Object, new HashSet<string>() { "java", "node", "powershell" }, probingPaths, optionsMonitor);

            var result = workerConfigurationResolver.GetWorkerConfigPaths();

            // Assert
            Assert.Equal(result.Count, 5);
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, "java"))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, "node"))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, "powershell"))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, "dotnet-isolated"))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, "python"))));
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
        [InlineData(null, "LATEST", "dotnet-isolated")]
        [InlineData(null, "STANDARD", "dotnet-isolated")]
        [InlineData("Empty", "LATEST", "dotnet-isolated")]
        [InlineData("Empty", "STANDARD", "dotnet-isolated")]
        public void GetWorkerConfigs_NullOREmptyProbingPath_ReturnsExpectedConfigs(string probingPathValue, string releaseChannel, string languageWorker)
        {
            // Arrange
            var mockEnv = new Mock<IEnvironment>();
            mockEnv.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns(languageWorker);
            mockEnv.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);

            List<string> probingPaths = null;

            if (probingPathValue == "Empty")
            {
                probingPaths = new List<string>();
            }

            var mockProfileManager = new Mock<IWorkerProfileManager>();
            var mockConfig = new Mock<IConfiguration>();
            var mockLogger = new Mock<ILogger>();

            var testScriptHostManager = new Mock<IScriptHostManager>();
            var resolverOptionssetup = new WorkerConfigurationResolverOptionsSetup(_mockConfig.Object, mockEnv.Object, testScriptHostManager.Object);
            var resolverOptions = new WorkerConfigurationResolverOptions();
            resolverOptionssetup.Configure(resolverOptions);

            var factory = new TestOptionsFactory<WorkerConfigurationResolverOptions>(resolverOptions);
            var source = new TestChangeTokenSource<WorkerConfigurationResolverOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<WorkerConfigurationResolverOptions>(factory, changeTokens, factory);

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(_mockLogger.Object, FileUtility.Instance, _mockProfileManager.Object, new HashSet<string>() { "java", "node", "powershell" }, probingPaths, optionsMonitor);

            var result = workerConfigurationResolver.GetWorkerConfigPaths();

            // Assert
            Assert.Equal(result.Count, 1);
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, languageWorker))));
        }
    }
}