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
        private readonly ILoggerFactory _loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
        private TestMetricsLogger _testMetricsLogger;
        private ScriptApplicationHostOptions _scriptApplicationHostOptions;
        private AggregateFunctionMetadataProvider _aggregateFunctionMetadataProvider;

        public WorkerFunctionMetadataProviderTests()
        {
            _testMetricsLogger = new TestMetricsLogger();
            _scriptApplicationHostOptions = new ScriptApplicationHostOptions();
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
        public void GetFunctionMetadataAsync_MixedApp()
        {
            TestLogger logger = new TestLogger("FunctionDispatcherTests");
            var function = GetTestRawFunctionMetadata(useDefaultMetadataIndexing: true);
            IEnumerable<RawFunctionMetadata> rawFunctionMetadataCollection = new List<RawFunctionMetadata>() { function };
            var functionMetadataCollection = new List<FunctionMetadata>();
            functionMetadataCollection.Add(GetTestFunctionMetadata());

            Mock<IFunctionInvocationDispatcher> mockRpcFunctionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>();
            mockRpcFunctionInvocationDispatcher.Setup(m => m.GetWorkerMetadata()).Returns(Task.FromResult(rawFunctionMetadataCollection));
            mockRpcFunctionInvocationDispatcher.Setup(m => m.InitializeAsync(functionMetadataCollection, default));
            mockRpcFunctionInvocationDispatcher.Setup(m => m.FinishInitialization(functionMetadataCollection, default));

            Mock<IFunctionMetadataProvider> mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            var configs = TestHelpers.GetTestWorkerConfigs().ToImmutableArray();
            var env = SystemEnvironment.Instance;
            env.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, "EnableWorkerIndexing");
            foreach (var config in configs)
            {
                config.Description.WorkerIndexing = "true";
            }

            mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(configs, env, false)).Returns(Task.FromResult(functionMetadataCollection.ToImmutableArray()));

            var log = _loggerFactory.CreateLogger<AggregateFunctionMetadataProvider>();

            _aggregateFunctionMetadataProvider = new AggregateFunctionMetadataProvider(
                logger,
                mockRpcFunctionInvocationDispatcher.Object,
                mockFunctionMetadataProvider.Object);

            var abc = _aggregateFunctionMetadataProvider.GetFunctionMetadataAsync(configs, env, false).GetAwaiter().GetResult();

            var traces = logger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "Fallback to host indexing as worker denied indexing"));
            Assert.True(functionLoadLogs.Any());
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
