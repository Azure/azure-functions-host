// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WorkerFunctionMetadataProviderTests
    {
        private readonly TestLogger _logger;
        private AggregateFunctionMetadataProvider _aggregateFunctionMetadataProvider;
        private Mock<IFunctionInvocationDispatcher> _mockRpcFunctionInvocationDispatcher;
        private Mock<IFunctionMetadataProvider> _mockFunctionMetadataProvider;

        public WorkerFunctionMetadataProviderTests()
        {
            _logger = new TestLogger("WorkerFunctionMetadataProviderTests");
            _mockRpcFunctionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>();
            _mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
        }

        [Fact]
        public void ValidateBindings_NoBindings_Throws()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();

            var ex = Assert.Throws<FormatException>(() =>
            {
                AggregateFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal("At least one binding must be declared.", ex.Message);
        }

        [Fact]
        public void ValidateBindings_DuplicateBindingNames_Throws()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();
            rawBindings.Add("{\"type\": \"BlobTrigger\",\"name\": \"test\",\"direction\": \"in\", \"blobPath\": \"test\"}");
            rawBindings.Add("{\"type\": \"BlobTrigger\",\"name\": \"dupe\",\"direction\": \"in\"}");
            rawBindings.Add("{\"type\": \"BlobTrigger\",\"name\": \"dupe\",\"direction\": \"in\"}");

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                AggregateFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal("Multiple bindings with name 'dupe' discovered. Binding names must be unique.", ex.Message);
        }

        [Fact]
        public void ValidateBindings_NoTriggerBinding_Throws()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();
            rawBindings.Add("{\"type\": \"Blob\",\"name\": \"test\"}");

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                AggregateFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal("No trigger binding specified. A function must have a trigger input binding.", ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("_binding")]
        [InlineData("binding-test")]
        [InlineData("binding name")]
        public void ValidateBindings_InvalidName_Throws(string bindingName)
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();
            rawBindings.Add("{\"type\": \"BlobTrigger\",\"name\": \"dupe\",\"direction\": \"in\"}");
            rawBindings.Add("{\"type\": \"Blob\",\"name\": \"" + bindingName + "\"}");

            var ex = Assert.Throws<ArgumentException>(() =>
            {
                AggregateFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal($"The binding name {bindingName} is invalid. Please assign a valid name to the binding.", ex.Message);
        }

        [Theory]
        [InlineData("bindingName")]
        [InlineData("binding1")]
        [InlineData(ScriptConstants.SystemReturnParameterBindingName)]
        public void ValidateBindings_ValidName_DoesNotThrow(string bindingName)
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();
            rawBindings.Add("{\"type\": \"BlobTrigger\",\"name\": \"dupe\",\"direction\": \"in\"}");

            if (bindingName == ScriptConstants.SystemReturnParameterBindingName)
            {
                rawBindings.Add("{\"type\": \"Blob\",\"name\": \"" + bindingName + "\", \"direction\": \"out\"}");
            }
            else
            {
                rawBindings.Add("{\"type\": \"Blob\",\"name\": \"" + bindingName + "\"}");
            }

            try
            {
                AggregateFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            }
            catch (ArgumentException)
            {
                Assert.True(false, $"Valid binding name '{bindingName}' failed validation.");
            }
        }

        [Fact]
        public void ValidateBindings_OutputNameWithoutDirection_Throws()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();
            rawBindings.Add("{\"type\": \"BlobTrigger\",\"name\": \"dupe\",\"direction\": \"in\"}");
            rawBindings.Add("{\"type\": \"Blob\",\"name\": \"" + ScriptConstants.SystemReturnParameterBindingName + "\"}");

            var ex = Assert.Throws<ArgumentException>(() =>
            {
                AggregateFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal($"{ScriptConstants.SystemReturnParameterBindingName} bindings must specify a direction of 'out'.", ex.Message);
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

            var environment = SystemEnvironment.Instance;
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, "EnableWorkerIndexing");

            _mockRpcFunctionInvocationDispatcher.Setup(m => m.InitializeAsync(functionMetadataCollection, default)).Returns(Task.FromResult(0));
            _mockRpcFunctionInvocationDispatcher.Setup(m => m.GetWorkerMetadata()).Returns(Task.FromResult(rawFunctionMetadataCollection));
            _mockRpcFunctionInvocationDispatcher.Setup(m => m.FinishInitialization(functionMetadataCollection, default)).Returns(Task.FromResult(0));
            _mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, environment, false)).Returns(Task.FromResult(functionMetadataCollection.ToImmutableArray()));

            _aggregateFunctionMetadataProvider = new AggregateFunctionMetadataProvider(_logger, _mockRpcFunctionInvocationDispatcher.Object, _mockFunctionMetadataProvider.Object);

            // Act
            var functions = _aggregateFunctionMetadataProvider.GetFunctionMetadataAsync(workerConfigs, environment, false).GetAwaiter().GetResult();

            // Assert
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
            var environment = SystemEnvironment.Instance;
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");

            _mockRpcFunctionInvocationDispatcher.Setup(m => m.InitializeAsync(functionMetadataCollection, default)).Returns(Task.FromResult(0));
            _mockRpcFunctionInvocationDispatcher.Setup(m => m.GetWorkerMetadata()).Returns(Task.FromResult(rawFunctionMetadataCollection));
            _mockRpcFunctionInvocationDispatcher.Setup(m => m.FinishInitialization(functionMetadataCollection, default)).Returns(Task.FromResult(0));
            _mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, environment, false)).Returns(Task.FromResult(functionMetadataCollection.ToImmutableArray()));

            _aggregateFunctionMetadataProvider = new AggregateFunctionMetadataProvider(_logger, _mockRpcFunctionInvocationDispatcher.Object, _mockFunctionMetadataProvider.Object);

            //Act
            var functions = _aggregateFunctionMetadataProvider.GetFunctionMetadataAsync(workerConfigs, environment, false).GetAwaiter().GetResult();

            // Assert
            var traces = _logger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "Fallback to host indexing as worker denied indexing"));
            Assert.False(functionLoadLogs.Any());
        }

        [Fact]
        public void GetFunctionMetadataAsync_WorkerIndexing_NoHostFallback()
        {
            // Arrange
            _logger.ClearLogMessages();

            IEnumerable<RawFunctionMetadata> rawFunctionMetadataCollection = new List<RawFunctionMetadata>();
            var functionMetadataCollection = new List<FunctionMetadata>();

            var workerConfigs = TestHelpers.GetTestWorkerConfigs().ToImmutableArray();
            workerConfigs.ToList().ForEach(config => config.Description.WorkerIndexing = "true");

            var environment = SystemEnvironment.Instance;
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, "EnableWorkerIndexing");
            workerConfigs.ToList().ForEach(config => config.Description.WorkerIndexing = "true");

            _mockRpcFunctionInvocationDispatcher.Setup(m => m.InitializeAsync(functionMetadataCollection, default)).Returns(Task.FromResult(0));
            _mockRpcFunctionInvocationDispatcher.Setup(m => m.GetWorkerMetadata()).Returns(Task.FromResult(rawFunctionMetadataCollection));
            _mockRpcFunctionInvocationDispatcher.Setup(m => m.FinishInitialization(functionMetadataCollection, default)).Returns(Task.FromResult(0));
            _mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, environment, false)).Returns(Task.FromResult(functionMetadataCollection.ToImmutableArray()));

            _aggregateFunctionMetadataProvider = new AggregateFunctionMetadataProvider(_logger, _mockRpcFunctionInvocationDispatcher.Object, _mockFunctionMetadataProvider.Object);

            // Act
            var functions = _aggregateFunctionMetadataProvider.GetFunctionMetadataAsync(workerConfigs, environment, false).GetAwaiter().GetResult();

            // Assert
            var traces = _logger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "Fallback to host indexing as worker denied indexing"));
            Assert.False(functionLoadLogs.Any());
            Assert.True(functions.Count() == 0);
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
