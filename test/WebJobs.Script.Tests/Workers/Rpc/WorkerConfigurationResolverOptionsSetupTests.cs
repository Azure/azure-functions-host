// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class WorkerConfigurationResolverOptionsSetupTests
    {
        [Fact]
        public void Configure_WithRealEnvironmentValues_SetsCorrectDefaults()
        {
            // Arrange
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

            var setup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();

            // Act
            setup.Configure(options);

            // Assert
            Assert.Null(options.WorkerRuntime);
            Assert.Equal(ScriptConstants.LatestPlatformChannelNameUpper, options.ReleaseChannel);
            Assert.False(options.IsPlaceholderModeEnabled);
            Assert.False(options.IsMultiLanguageWorkerEnvironment);
            Assert.Equal("/default/workers", options.WorkersDirPath);
            Assert.NotNull(options.LanguageWorkersSettings);

            Assert.Equal(2, options.ProbingPaths.Count);
            Assert.True(options.ProbingPaths.Contains("testPath1"));
            Assert.True(options.ProbingPaths.Contains("testPath2"));

            Assert.False(options.WorkersAvailableForResolution.Any());
        }

        [Fact]
        public void Configure_WithRealEnvironmentValues_SetsCorrectDefaults1()
        {
            // Arrange
            var testEnvironment = new TestEnvironment();
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

            var setup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();

            // Act
            setup.Configure(options);

            // Assert
            Assert.Equal("java", options.WorkerRuntime);
            Assert.Equal("standard", options.ReleaseChannel);
            Assert.False(options.IsPlaceholderModeEnabled);
            Assert.False(options.IsMultiLanguageWorkerEnvironment);
            Assert.Equal("/default/workers", options.WorkersDirPath);
            Assert.NotNull(options.LanguageWorkersSettings);

            Assert.NotNull(options.ProbingPaths);
            Assert.False(options.ProbingPaths.Any());

            Assert.True(options.WorkersAvailableForResolution.Count == 2);
            Assert.True(options.WorkersAvailableForResolution.Contains("java"));
            Assert.True(options.WorkersAvailableForResolution.Contains("node"));
        }
    }
}