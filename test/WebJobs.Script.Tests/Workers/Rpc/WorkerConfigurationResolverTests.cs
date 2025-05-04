// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public class WorkerConfigurationResolverTests
    {
        private readonly Mock<IEnvironment> _mockEnvironment;
        private readonly Mock<IWorkerProfileManager> _mockProfileManager;
        private readonly Mock<IConfiguration> _mockConfig;
        private readonly Mock<ILogger> _mockLogger;

        public WorkerConfigurationResolverTests()
        {
            _mockEnvironment = new Mock<IEnvironment>();
            _mockProfileManager = new Mock<IWorkerProfileManager>();
            _mockConfig = new Mock<IConfiguration>();
            _mockLogger = new Mock<ILogger>();
        }

        [Fact]
        public void GetWorkerConfigs_ReturnsExpectedConfigs()
        {
            // Arrange
            var probingPaths = new List<string> { "C:\\FunctionsRepos\\Host\\azure-functions-host\\src\\WebJobs.Script\\TestWorkers\\ProbingPaths\\workers\\" };
            var fallbackPath = "C:\\FunctionsRepos\\Host\\azure-functions-host\\src\\WebJobs.Script\\TestWorkers\\FallbackPath\\workers\\";
            //_mockEnvironment.Setup(e => e.GetEnvironmentVariable(It.IsAny<string>())).Returns("test-value");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns("java");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns("LATEST");

            // Mock directory structure
            //    Directory.CreateDirectory("c:\\testfolder\\workers\\java\\1.1");
            //    File.WriteAllText("c:\\testfolder\\workers\\java\\1.1\\worker.config.json", "{}");

            // Act
            var workerConfigurationResolver = new WorkerConfigurationResolver(_mockConfig.Object, _mockLogger.Object, _mockEnvironment.Object, _mockProfileManager.Object);

            var result = workerConfigurationResolver.GetWorkerConfigs(probingPaths, fallbackPath);

            // Assert
            Assert.Single(result);
            Assert.Contains("C:\\FunctionsRepos\\Host\\azure-functions-host\\src\\WebJobs.Script\\TestWorkers\\ProbingPaths\\workers\\java", result);

            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns((string)null);

            // Cleanup
            Directory.Delete("c:\\testfolder", true);
        }

        [Fact]
        public void GetWorkerConfigs_ReturnsExpectedConfigs1()
        {
            // Arrange
            var probingPaths = new List<string> { "c:\\testfolder\\probingpaths\\workers\\" };
            var fallbackPath = "c:\\testfolder\\fallback\\workers\\";

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
            Directory.Delete("c:\\testfolder", true);
        }

        [Fact]
        public void IsCompatibleWithHost_ReturnsTrue_WhenCapabilitiesMatch()
        {
            // Arrange
            var hostCapabilities = new HashSet<string> { "test-capability-1", "test-capability-2" };
            var workerConfigPath = "c:\\testfolder\\workers\\java\\1.1\\worker.config.json";
            var workerDir = "c:\\testfolder\\workers\\java\\1.1";

            Directory.CreateDirectory(workerDir);
            File.WriteAllText(workerConfigPath, @"
        {
            ""hostRequirements"": [""test-capability-1"", ""test-capability-2""],
            ""description"": { ""language"": ""java"" }
        }");

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
            Directory.Delete("c:\\testfolder", true);
        }

        [Fact]
        public void IsCompatibleWithHost_ReturnsFalse_WhenCapabilitiesDoNotMatch()
        {
            // Arrange
            var hostCapabilities = new HashSet<string> { "test-capability-1" };
            var workerConfigPath = "c:\\testfolder\\workers\\java\\1.1\\worker.config.json";
            var workerDir = "c:\\testfolder\\workers\\java\\1.1";

            Directory.CreateDirectory(workerDir);
            File.WriteAllText(workerConfigPath, @"
        {
            ""hostRequirements"": [""test-capability-1"", ""test-capability-2""],
            ""description"": { ""language"": ""java"" }
        }");

            // Act
            var workerConfigurationResolver = new WorkerConfigurationResolver(_mockConfig.Object, _mockLogger.Object, _mockEnvironment.Object, _mockProfileManager.Object);

            var result = workerConfigurationResolver.IsCompatibleWithHost(workerDir);

            // Assert
            Assert.False(result);

            // Cleanup
            Directory.Delete("c:\\testfolder", true);
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
    }
}