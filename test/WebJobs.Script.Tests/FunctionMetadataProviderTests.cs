// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionMetadataProviderTests
    {
        private readonly TestLogger<FunctionMetadataProvider> _logger;
        private readonly IConfiguration _configuration = new ConfigurationBuilder().Build();
        private readonly Mock<IScriptHostManager> _scriptHostManager = new();
        private Mock<IWorkerFunctionMetadataProvider> _workerFunctionMetadataProvider;
        private Mock<IHostFunctionMetadataProvider> _hostFunctionMetadataProvider;

        public FunctionMetadataProviderTests()
        {
            _logger = new TestLogger<FunctionMetadataProvider>();
            _workerFunctionMetadataProvider = new Mock<IWorkerFunctionMetadataProvider>();
            _hostFunctionMetadataProvider = new Mock<IHostFunctionMetadataProvider>();
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_WorkerIndexing_HostFallback()
        {
            // Arrange
            _logger.ClearLogMessages();
            ImmutableArray<FunctionMetadata> functionMetadataCollection = GetTestFunctionMetadata();
            IList<RpcWorkerConfig> workerConfigs = TestHelpers.GetTestWorkerConfigs();
            foreach (RpcWorkerConfig config in workerConfigs)
            {
                config.Description.WorkerIndexing = "true";
            }

            TestEnvironment environment = new();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, "EnableWorkerIndexing");

            FunctionMetadataProvider defaultProvider = new(
                _logger,
                _workerFunctionMetadataProvider.Object,
                _hostFunctionMetadataProvider.Object,
                new OptionsWrapper<FunctionsHostingConfigOptions>(new FunctionsHostingConfigOptions()),
                environment,
                _configuration,
                _scriptHostManager.Object);

            FunctionMetadataResult result = new(true, functionMetadataCollection);
            _workerFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, false))
                .ReturnsAsync(result);
            _hostFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, false))
                .ReturnsAsync(functionMetadataCollection);

            // Act
            ImmutableArray<FunctionMetadata> functions = await defaultProvider
                .GetFunctionMetadataAsync(workerConfigs, false);

            // Assert
            Assert.Equal(1, functions.Length);
            Assert.Contains(
                _logger.GetLogMessages(),
                m => string.Equals(m.FormattedMessage, "Fallback to host indexing as worker denied indexing"));
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_HostIndexing()
        {
            // Arrange
            _logger.ClearLogMessages();
            ImmutableArray<FunctionMetadata> functionMetadataCollection = GetTestFunctionMetadata();
            IList<RpcWorkerConfig> workerConfigs = TestHelpers.GetTestWorkerConfigs();
            foreach (RpcWorkerConfig config in workerConfigs)
            {
                config.Description.WorkerIndexing = "true";
            }

            TestEnvironment environment = new();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, string.Empty);

            Mock<IWorkerFunctionMetadataProvider> workerMetadataProvider = new();
            workerMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false))
                .ReturnsAsync(new FunctionMetadataResult(true, []));

            FunctionMetadataProvider defaultProvider = new(
                _logger,
                workerMetadataProvider.Object,
                _hostFunctionMetadataProvider.Object,
                new OptionsWrapper<FunctionsHostingConfigOptions>(new FunctionsHostingConfigOptions()),
                environment,
                _configuration,
                _scriptHostManager.Object);

            FunctionMetadataResult result = new(true, functionMetadataCollection);
            _hostFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, false))
                .ReturnsAsync(functionMetadataCollection);

            // Act
            ImmutableArray<FunctionMetadata> functions = await defaultProvider
                .GetFunctionMetadataAsync(workerConfigs, false);

            // Assert
            Assert.Equal(1, functions.Length);
            Assert.Contains(
                _logger.GetLogMessages(),
                m => string.Equals(m.FormattedMessage, "Fallback to host indexing as worker denied indexing"));
        }

        [Theory]
        [InlineData("python", "web-function", null)]
        [InlineData("node", null, "WEB-FUNCTION")]
        [InlineData("java", null, "web-function")]
        public async Task GetFunctionMetadataAsync_WebFunction_UsesHostIndexing(
            string runtime, string rootProfile, string activeProfile)
        {
            TestEnvironment environment = new();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, runtime);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, "EnableWorkerIndexing");
            RpcWorkerConfig[] workerConfigs =
            [
                new() { Description = new RpcWorkerDescription { Language = runtime, WorkerIndexing = "true" } }
            ];
            FunctionsHostingConfigOptions hostingOptions = new();
            Assert.True(Utility.CanWorkerIndex(workerConfigs, environment, hostingOptions));

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["AzureFunctionsJobHost:configurationProfile"] = rootProfile
                })
                .Build();
            Mock<IScriptHostManager> hostManager = new();
            if (activeProfile is not null)
            {
                Mock<IServiceProvider> services = new();
                services.Setup(s => s.GetService(typeof(IOptions<ScriptJobHostOptions>)))
                    .Returns(Options.Create(new ScriptJobHostOptions { ConfigurationProfile = activeProfile }));
                hostManager.Setup(m => m.Services).Returns(services.Object);
            }

            ImmutableArray<FunctionMetadata> expected = GetTestFunctionMetadata();
            _hostFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, false)).ReturnsAsync(expected);
            FunctionMetadataProvider provider = new(
                _logger, _workerFunctionMetadataProvider.Object, _hostFunctionMetadataProvider.Object,
                Options.Create(hostingOptions), environment, configuration, hostManager.Object);

            ImmutableArray<FunctionMetadata> functions = await provider.GetFunctionMetadataAsync(workerConfigs);

            Assert.Equal(expected, functions);
            _workerFunctionMetadataProvider.Verify(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), It.IsAny<bool>()), Times.Never);
            _hostFunctionMetadataProvider.Verify(m => m.GetFunctionMetadataAsync(workerConfigs, false), Times.Once);
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_ActiveProfileTakesPrecedenceOverRootConfiguration()
        {
            TestEnvironment environment = new();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, "EnableWorkerIndexing");
            RpcWorkerConfig[] workerConfigs =
            [
                new() { Description = new RpcWorkerDescription { Language = "node", WorkerIndexing = "true" } }
            ];
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["AzureFunctionsJobHost:configurationProfile"] = "web-function"
                })
                .Build();
            Mock<IServiceProvider> services = new();
            services.Setup(s => s.GetService(typeof(IOptions<ScriptJobHostOptions>)))
                .Returns(Options.Create(new ScriptJobHostOptions()));
            _scriptHostManager.Setup(m => m.Services).Returns(services.Object);
            ImmutableArray<FunctionMetadata> expected = GetTestFunctionMetadata();
            _workerFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, false))
                .ReturnsAsync(new FunctionMetadataResult(false, expected));
            FunctionMetadataProvider provider = new(
                _logger, _workerFunctionMetadataProvider.Object, _hostFunctionMetadataProvider.Object,
                Options.Create(new FunctionsHostingConfigOptions()), environment, configuration, _scriptHostManager.Object);

            ImmutableArray<FunctionMetadata> functions = await provider.GetFunctionMetadataAsync(workerConfigs);

            Assert.Equal(expected, functions);
            _workerFunctionMetadataProvider.Verify(m => m.GetFunctionMetadataAsync(workerConfigs, false), Times.Once);
            _hostFunctionMetadataProvider.Verify(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), It.IsAny<bool>()), Times.Never);
        }

        private static ImmutableArray<FunctionMetadata> GetTestFunctionMetadata(string name = "testFunction")
        {
            return
            [
                new FunctionMetadata()
                {
                    Name = name,
                    Language = "node"
                }
            ];
        }
    }
}