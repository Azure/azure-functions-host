// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
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
    public class WorkerConfigurationResolverTests
    {
        private readonly string _probingPath1 = Path.GetFullPath("..\\..\\..\\..\\test\\TestWorkers\\ProbingPaths\\workers\\");
        private readonly string _fallbackPath = Path.GetFullPath("workers");

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

            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);

            var config = WorkerConfigurationResolverTestsHelper.GetConfigurationWithProbingPaths(probingPaths);

            var mockProfileManager = new Mock<IWorkerProfileManager>();
            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node");

            var optionsMonitor = WorkerConfigurationResolverTestsHelper.GetTestWorkerConfigurationResolverOptions(config, mockEnvironment.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(loggerFactory, fileSystem, mockProfileManager.Object, optionsMonitor);

            var result = workerConfigurationResolver.GetWorkerConfigPaths();

            // Assert
            Assert.Equal(result.Count, 5);
            Assert.True(result.Any(r => r.Contains(Path.Combine(_probingPath1, java))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_probingPath1, node))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, powershell))));
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
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);

            List<string> probingPaths = null;

            if (probingPathValue == "Empty")
            {
                probingPaths = new List<string>();
            }

            var mockProfileManager = new Mock<IWorkerProfileManager>();
            var config = WorkerConfigurationResolverTestsHelper.GetConfigurationWithProbingPaths(probingPaths);
            var fileSystem = new FileSystem();

            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node|powershell");
            var optionsMonitor = WorkerConfigurationResolverTestsHelper.GetTestWorkerConfigurationResolverOptions(config, mockEnvironment.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(loggerFactory, fileSystem, mockProfileManager.Object, optionsMonitor);

            var result = workerConfigurationResolver.GetConfigurationInfo().WorkerConfigPaths;

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

            var config = WorkerConfigurationResolverTestsHelper.GetConfigurationWithProbingPaths(probingPaths);

            var mockProfileManager = new Mock<IWorkerProfileManager>();
            var mockConfig = new Mock<IConfiguration>();

            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node|powershell");
            var optionsMonitor = WorkerConfigurationResolverTestsHelper.GetTestWorkerConfigurationResolverOptions(config, mockEnv.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));

            // Act
            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(loggerFactory, FileUtility.Instance, mockProfileManager.Object, optionsMonitor);

            var result = workerConfigurationResolver.GetWorkerConfigPaths();

            // Assert
            Assert.Equal(result.Count, 1);
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, languageWorker))));
        }

        [Theory]
        [InlineData("LATEST", "java\\2.18.0", "node\\3.10.1", "powershell", "dotnet-isolated", "python")]
        public void GetWorkerConfigs_MultiLanguageWorker_ReturnsExpectedConfigs1(string releaseChannel, string java, string node, string powershell, string dotnetIsolated, string python)
        {
            // Arrange
            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel)).Returns(releaseChannel);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AppKind)).Returns(ScriptConstants.WorkFlowAppKind);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime)).Returns((string)null);

            Dictionary<string, HashSet<Version>> ignoredVersions = new Dictionary<string, HashSet<Version>>
            {
                { "java", new HashSet<Version> { new Version("2.19.0") } }
            };

            var mockProfileManager = new Mock<IWorkerProfileManager>();
            var mockConfig = new Mock<IConfiguration>();

            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node|powershell");
            var optionsMonitor = WorkerConfigurationResolverTestsHelper.GetTestWorkerConfigurationResolverOptions(mockConfig.Object, mockEnvironment.Object, testScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));

            var workerConfigurationResolver = new DynamicWorkerConfigurationResolver(loggerFactory, FileUtility.Instance, mockProfileManager.Object, optionsMonitor);

            var result = workerConfigurationResolver.GetWorkerConfigPaths();

            // Assert
            Assert.Equal(result.Count, 5);
            Assert.True(result.Any(r => r.Contains(Path.Combine(_probingPath1, java))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_probingPath1, node))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, powershell))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, dotnetIsolated))));
            Assert.True(result.Any(r => r.Contains(Path.Combine(_fallbackPath, python))));
        }
    }
}