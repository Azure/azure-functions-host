// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
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
        public void GetFunctionMetadataAsync_WorkerIndexing_HostFallback()
        {
            // Arrange
            _logger.ClearLogMessages();

            var function = GetTestRawFunctionMetadata(useDefaultMetadataIndexing: true);
            IEnumerable<RawFunctionMetadata> rawFunctionMetadataCollection = new List<RawFunctionMetadata>() { function };
            var functionMetadataCollection = new List<FunctionMetadata>();
            functionMetadataCollection.Add(GetTestFunctionMetadata());

            var workerConfigs = TestHelpers.GetTestWorkerConfigs().ToImmutableArray();
            workerConfigs.ToList().ForEach(config => config.Description.WorkerIndexing = "true");
            var scriptjobhostoptions = new ScriptJobHostOptions();
            scriptjobhostoptions.RootScriptPath = Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "node");

            var environment = SystemEnvironment.Instance;
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, "EnableWorkerIndexing");

            var defaultProvider = new FunctionMetadataProvider(_logger, _workerFunctionMetadataProvider.Object, _hostFunctionMetadataProvider.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(new FunctionsHostingConfigOptions()), SystemEnvironment.Instance);

            FunctionMetadataResult result = new FunctionMetadataResult(true, functionMetadataCollection.ToImmutableArray());
            _workerFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, false)).Returns(Task.FromResult(result));
            _hostFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, environment, false)).Returns(Task.FromResult(functionMetadataCollection.ToImmutableArray()));

            // Act
            var functions = defaultProvider.GetFunctionMetadataAsync(workerConfigs, environment, false).GetAwaiter().GetResult();

            // Assert
            Assert.Equal(1, functions.Length);
            var traces = _logger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "Fallback to host indexing as worker denied indexing"));
            Assert.True(functionLoadLogs.Any());
        }

        [Fact]
        public void GetFunctionMetadataAsync_HostIndexing()
        {
            // Arrange
            _logger.ClearLogMessages();

            var function = GetTestRawFunctionMetadata(useDefaultMetadataIndexing: true);
            IEnumerable<RawFunctionMetadata> rawFunctionMetadataCollection = new List<RawFunctionMetadata>() { function };
            var functionMetadataCollection = new List<FunctionMetadata>();
            functionMetadataCollection.Add(GetTestFunctionMetadata());

            var workerConfigs = TestHelpers.GetTestWorkerConfigs().ToImmutableArray();
            workerConfigs.ToList().ForEach(config => config.Description.WorkerIndexing = "true");
            var scriptjobhostoptions = new ScriptJobHostOptions();
            scriptjobhostoptions.RootScriptPath = Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "node");

            var environment = SystemEnvironment.Instance;
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, string.Empty);
            var optionsMonitor = TestHelpers.CreateOptionsMonitor(new FunctionsHostingConfigOptions());
            var defaultProvider = new FunctionMetadataProvider(_logger, _workerFunctionMetadataProvider.Object, _hostFunctionMetadataProvider.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(new FunctionsHostingConfigOptions()), SystemEnvironment.Instance);

            FunctionMetadataResult result = new FunctionMetadataResult(true, functionMetadataCollection.ToImmutableArray());
            _hostFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, environment, false)).Returns(Task.FromResult(functionMetadataCollection.ToImmutableArray()));

            // Act
            var functions = defaultProvider.GetFunctionMetadataAsync(workerConfigs, environment, false).GetAwaiter().GetResult();

            // Assert
            Assert.Equal(1, functions.Length);
            var traces = _logger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "Fallback to host indexing as worker denied indexing"));
            Assert.False(functionLoadLogs.Any());
        }

        private static RawFunctionMetadata GetTestRawFunctionMetadata(bool useDefaultMetadataIndexing)
        {
            return new RawFunctionMetadata()
            {
                UseDefaultMetadataIndexing = useDefaultMetadataIndexing
            };
        }

        private static FunctionMetadata GetTestFunctionMetadata(string name = "testFunction")
        {
            return new FunctionMetadata()
            {
                Name = name,
                Language = "node"
            };
        }
    }
}