// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionMetadataProviderTests
    {
        private readonly TestLogger<FunctionMetadataProvider> _logger;
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
                environment);

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
                environment);

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