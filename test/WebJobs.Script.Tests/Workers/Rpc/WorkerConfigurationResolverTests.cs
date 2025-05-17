// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public class WorkerConfigurationResolverTests
    {
        private readonly Mock<IWorkerProfileManager> _mockProfileManager;
        private readonly Mock<IConfiguration> _mockConfig;
        private readonly Mock<ILogger> _mockLogger;
        private readonly string _probingPath1 = "C:\\FunctionsRepos\\Host\\azure-functions-host\\test\\TestWorkers\\ProbingPaths\\workers\\";
        private readonly string _fallbackPath = "C:\\FunctionsRepos\\Host\\azure-functions-host\\test\\TestWorkers\\FallbackPath\\workers\\";
        private List<string> _probingPaths;

        public WorkerConfigurationResolverTests()
        {
            _mockProfileManager = new Mock<IWorkerProfileManager>();
            _mockConfig = new Mock<IConfiguration>();
            _mockLogger = new Mock<ILogger>();

            _probingPaths = new List<string> { _probingPath1 };
        }

        [Theory]
        [InlineData("java", "LATEST", "2.19.0")]
        [InlineData("java", "STANDARD", "2.18.0")]
        [InlineData("node", "STANDARD", "3.10.1")]
        public void GetWorkerConfigs_ReturnsExpectedConfigs(string languageWorker, string releaseChannel, string version)
        {
            // Arrange
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns(languageWorker);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns((string)null);

            // Act
            var workerConfigurationResolver = new WorkerConfigurationResolver(_mockConfig.Object, _mockLogger.Object, mockEnvironment.Object, _mockProfileManager.Object);

            var result = workerConfigurationResolver.GetWorkerConfigs(_probingPaths, _fallbackPath);

            // Assert
            Assert.Single(result);
            Assert.Contains(_probingPath1 + languageWorker + "\\" + version, result);
        }

        [Theory]
        [InlineData("LATEST", "java\\2.19.0", "node\\3.10.1", "powershell\\7.4", "dotnet-isolated")]
        [InlineData("STANDARD", "java\\2.18.0", "node\\3.10.1", "powershell\\7.2", "dotnet-isolated")]
        public void GetWorkerConfigs_MultiLanguageWorker_ReturnsExpectedConfigs(string releaseChannel, string java, string node, string powershell, string dotnetIsolated)
        {
            // Arrange
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);

            // Act
            var workerConfigurationResolver = new WorkerConfigurationResolver(_mockConfig.Object, _mockLogger.Object, mockEnvironment.Object, _mockProfileManager.Object);

            var result = workerConfigurationResolver.GetWorkerConfigs(_probingPaths, _fallbackPath);

            // Assert
            Assert.Equal(result.Count, 4);
            Assert.True(result.Any(r => r.Contains(_probingPath1 + java)));
            Assert.True(result.Any(r => r.Contains(_probingPath1 + node)));
            Assert.True(result.Any(r => r.Contains(_probingPath1 + powershell)));
            Assert.True(result.Any(r => r.Contains(_fallbackPath + dotnetIsolated)));
        }

        [Theory]
        [InlineData(null, "LATEST", "java", "node", "powershell", "dotnet-isolated")]
        [InlineData(null, "STANDARD", "java", "node", "powershell", "dotnet-isolated")]
        [InlineData("Empty", "LATEST", "java", "node", "powershell", "dotnet-isolated")]
        [InlineData("Empty", "STANDARD", "java", "node", "powershell", "dotnet-isolated")]
        public void GetWorkerConfigs_MultiLanguageWorker_NullOREmptyProbingPath_ReturnsExpectedConfigs(string probingPathValue, string releaseChannel, string java, string node, string powershell, string dotnetIsolated)
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

            // Act
            var workerConfigurationResolver = new WorkerConfigurationResolver(_mockConfig.Object, _mockLogger.Object, mockEnvironment.Object, _mockProfileManager.Object);

            var result = workerConfigurationResolver.GetWorkerConfigs(probingPaths, _fallbackPath);

            // Assert
            Assert.Equal(result.Count, 4);
            Assert.True(result.Any(r => r.Contains(_fallbackPath + java)));
            Assert.True(result.Any(r => r.Contains(_fallbackPath + node)));
            Assert.True(result.Any(r => r.Contains(_fallbackPath + powershell)));
            Assert.True(result.Any(r => r.Contains(_fallbackPath + dotnetIsolated)));
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
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns(languageWorker);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns((string)null);

            List<string> probingPaths = null;

            if (probingPathValue == "Empty")
            {
                probingPaths = new List<string>();
            }

            // Act
            var workerConfigurationResolver = new WorkerConfigurationResolver(_mockConfig.Object, _mockLogger.Object, mockEnvironment.Object, _mockProfileManager.Object);

            var result = workerConfigurationResolver.GetWorkerConfigs(probingPaths, _fallbackPath);

            // Assert
            Assert.Equal(result.Count, 1);
            Assert.True(result.Any(r => r.Contains(_fallbackPath + languageWorker)));
        }

        /*
        [Fact]
     //   [InlineData("java", "LATEST", "2.19.0")]
       // [InlineData("java", "STANDARD", "2.18.1")]
      //  [InlineData("node", "STANDARD", "3.10.1")]
        public void GetWorkerConfigs_CompatibilityCheck_ReturnsExpectedConfigs() //string languageWorker, string releaseChannel, string version)
        {
            // Arrange
            var probingPaths = new List<string> { "C:\\FunctionsRepos\\Host\\azure-functions-host\\test\\TestWorkers\\ProbingPaths\\workers\\" };
            var fallbackPath = "C:\\FunctionsRepos\\Host\\azure-functions-host\\test\\TestWorkers\\FallbackPath\\workers\\";

            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns("java");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns("STANDARD");

            // Act
            var workerConfigurationResolver = new WorkerConfigurationResolver(_mockConfig.Object, _mockLogger.Object, _mockEnvironment.Object, _mockProfileManager.Object);

            var result = workerConfigurationResolver.GetWorkerConfigs(probingPaths, fallbackPath);

            // Assert
            Assert.Single(result);
            Assert.Contains("C:\\FunctionsRepos\\Host\\azure-functions-host\\test\\TestWorkers\\ProbingPaths\\workers\\java\\2.18.0", result);

            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns((string)null);
        }

        [Fact]
        public void GetWorkerConfigs_ReturnsExpectedConfigs1()
        {
            // Arrange
            var probingPaths = new List<string> { "C:\\FunctionsRepos\\Host\\azure-functions-host\\test\\TestWorkers\\ProbingPaths\\workers\\" };
            var fallbackPath = "C:\\FunctionsRepos\\Host\\azure-functions-host\\test\\TestWorkers\\FallbackPath\\workers\\";

            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns("java");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns("LATEST");

            // Mock directory structure
            // CopyDirectory("C:\\FunctionsRepos\\Host\\azure-functions-host\\src\\WebJobs.Script\\TestWorkers\\ProbingPaths\\workers\\java\\", "c:\\testfolder\\probingpaths\\workers\\java");

            string text = @"
                {
                    ""hostRequirements"": [""test-capability-1"", ""test-capability-2""],
                    ""description"": { ""language"": ""java"", ""defaultExecutablePath"": ""%JAVA_HOME%/bin/java"" }
                }";

            Directory.CreateDirectory("c:\\testfolder\\probingpaths\\workers\\java\\1.1");
            File.WriteAllText("c:\\testfolder\\probingpaths\\workers\\java\\1.1\\worker.config.json", text);

            // Act
            var workerConfigurationResolver = new WorkerConfigurationResolver(_mockConfig.Object, _mockLogger.Object, _mockEnvironment.Object, _mockProfileManager.Object);

            var result = workerConfigurationResolver.GetWorkerConfigs(probingPaths, fallbackPath);

            // Assert
            Assert.Single(result);
            Assert.Contains("c:\\testfolder\\probingpaths\\workers\\java\\1.1", result);

            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns((string)null);

            // Cleanup
          //  Directory.Delete("c:\\testfolder", true);
        }

        [Fact]
        public void IsCompatibleWithHost_ReturnsTrue_WhenCapabilitiesMatch()
        {
            // Arrange
            var hostCapabilities = new HashSet<string> { "test-capability-1", "test-capability-2" };
            var workerConfigPath = "c:\\testfolder\\workers\\java\\1.1\\worker.config.json";
            var workerDir = "c:\\testfolder\\workers\\java\\1.1";

            string text = @"
                {
                    ""hostRequirements"": [""test-capability-1"", ""test-capability-2""],
                    ""description"": { ""language"": ""java"", ""defaultExecutablePath"": ""%JAVA_HOME%/bin/java"" }
                }";

            Directory.CreateDirectory(workerDir);
            File.WriteAllText(workerConfigPath, text);

            _mockProfileManager.Setup(p => p.LoadWorkerDescriptionFromProfiles(It.IsAny<RpcWorkerDescription>(), out It.Ref<RpcWorkerDescription>.IsAny))
                .Callback((RpcWorkerDescription _, out RpcWorkerDescription desc) =>
                {
                    desc = new RpcWorkerDescription { IsDisabled = false };
                });

            // Act
            var workerConfigurationResolver = new WorkerConfigurationResolver(_mockConfig.Object, _mockLogger.Object, _mockEnvironment.Object, _mockProfileManager.Object);

            var result = workerConfigurationResolver.IsCompatibleWithHost(workerDir);

            // Assert
            Assert.True(result);

            // Cleanup
          //  Directory.Delete("c:\\testfolder", true);
        }

        [Fact]
        public void IsCompatibleWithHost_ReturnsFalse_WhenCapabilitiesDoNotMatch()
        {
            // Arrange
            var hostCapabilities = new HashSet<string> { "test-capability-1" };
            var workerConfigPath = "c:\\testfolder\\workers\\java\\1.1\\worker.config.json";
            var workerDir = "c:\\testfolder\\workers\\java\\1.1";

            string text = @"
                {
                    ""hostRequirements"": [""test-capability-1"", ""test-capability-2""],
                    ""description"": { ""language"": ""java"", ""defaultExecutablePath"": ""%JAVA_HOME%/bin/java"" }
                }";

            Directory.CreateDirectory(workerDir);
            File.WriteAllText(workerConfigPath, text);

            // Act
            var workerConfigurationResolver = new WorkerConfigurationResolver(_mockConfig.Object, _mockLogger.Object, _mockEnvironment.Object, _mockProfileManager.Object);

            var result = workerConfigurationResolver.IsCompatibleWithHost(workerDir);

            // Assert
            Assert.False(result);

            // Cleanup
         //   Directory.Delete("c:\\testfolder", true);
        }

        public static void CopyDirectory(string sourceDir, string destinationDir)
        {
            // Ensure the destination directory exists
            Directory.CreateDirectory(destinationDir);

            // Copy all files from the source directory to the destination directory
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            // Recursively copy all subdirectories
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }
        */
    }
}