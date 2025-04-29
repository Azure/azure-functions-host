// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

public class WorkerConfigurationResolverTests
{
    private readonly Mock<IEnvironment> _mockEnvironment;
    private readonly Mock<IWorkerProfileManager> _mockProfileManager;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger> _mockLogger;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public WorkerConfigurationResolverTests()
    {
        _mockEnvironment = new Mock<IEnvironment>();
        _mockProfileManager = new Mock<IWorkerProfileManager>();
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger>();
        _jsonSerializerOptions = new JsonSerializerOptions();
    }

    [Fact]
    public void GetWorkerConfigs_ReturnsExpectedConfigs()
    {
        // Arrange
        var probingPaths = new List<string> { "c:\\testfolder\\workers" };
        var fallbackPath = "c:\\testfolder\\fallback";
        _mockEnvironment.Setup(e => e.GetEnvironmentVariable(It.IsAny<string>())).Returns("test-value");

        // Mock directory structure
        Directory.CreateDirectory("c:\\testfolder\\workers\\java\\1.1");
        File.WriteAllText("c:\\testfolder\\workers\\java\\1.1\\worker.config.json", "{}");

        // Act
        var result = WorkerConfigurationResolver.GetWorkerConfigs(
            probingPaths,
            fallbackPath,
            _mockEnvironment.Object,
            _jsonSerializerOptions,
            _mockProfileManager.Object,
            _mockConfig.Object,
            _mockLogger.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains("c:\\testfolder\\workers\\java\\1.1\\worker.config.json", result);

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
        var result = WorkerConfigurationResolver.IsCompatibleWithHost(
            hostCapabilities,
            workerConfigPath,
            _jsonSerializerOptions,
            workerDir,
            _mockProfileManager.Object,
            _mockConfig.Object,
            _mockLogger.Object);

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
        var result = WorkerConfigurationResolver.IsCompatibleWithHost(
            hostCapabilities,
            workerConfigPath,
            _jsonSerializerOptions,
            workerDir,
            _mockProfileManager.Object,
            _mockConfig.Object,
            _mockLogger.Object);

        // Assert
        Assert.False(result);

        // Cleanup
        Directory.Delete("c:\\testfolder", true);
    }
}
