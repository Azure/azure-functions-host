// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WorkerFunctionMetadataProviderTests
    {
        private readonly WorkerFunctionMetadataProvider _workerFunctionMetadataProvider;

        public WorkerFunctionMetadataProviderTests()
        {
            var mockScriptOptions = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            var mockLogger = new Mock<ILogger<WorkerFunctionMetadataProvider>>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockChannelManager = new Mock<IWebHostRpcWorkerChannelManager>();
            var mockScriptHostManager = new Mock<IScriptHostManager>();

            _workerFunctionMetadataProvider = new WorkerFunctionMetadataProvider(
             mockScriptOptions.Object,
             mockLogger.Object,
             mockEnvironment.Object,
             mockChannelManager.Object,
             mockScriptHostManager.Object);
        }

        [Fact]
        public void ValidateBindings_NoBindings_Throws()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();

            var ex = Assert.Throws<FormatException>(() =>
            {
                _workerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
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
                _workerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
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
                _workerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal("No trigger binding specified. A function must have a trigger input binding.", ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
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
                _workerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal($"The binding name {bindingName} is invalid. Please assign a valid name to the binding. See https://aka.ms/azure-functions-binding-name-rules for more details.", ex.Message);
        }

        [Theory]
        [InlineData("__")]
        [InlineData("__binding")]
        [InlineData("binding__")]
        [InlineData("bind__ing")]
        [InlineData("__binding__")]
        [InlineData("_binding")]
        [InlineData("binding_")]
        [InlineData("_binding_")]
        [InlineData("_another_binding_test_")]
        [InlineData("long_binding_name_that_is_valid")]
        [InlineData("binding_name")]
        [InlineData("_")]
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
                _workerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            }
            catch (ArgumentException)
            {
                Assert.True(false, $"Valid binding name '{bindingName}' failed validation.");
            }
        }

        [Fact]
        public void ValidateFunctionAppFormat_InputMixedApp()
        {
            var logger = new TestLogger<WorkerFunctionMetadataProvider>();
            logger.ClearLogMessages();
            string scriptPath = Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "sample", "node");
            var environment = SystemEnvironment.Instance;
            _workerFunctionMetadataProvider.ValidateFunctionAppFormat(scriptPath, logger, environment);
            var traces = logger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => m.FormattedMessage.Contains("Detected mixed function app. Some functions may not be indexed"));
            Assert.True(functionLoadLogs.Any());
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
                _workerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal($"{ScriptConstants.SystemReturnParameterBindingName} bindings must specify a direction of 'out'.", ex.Message);
        }

        [Fact]
        public async void ValidateFunctionMetadata_Logging()
        {
            var logger = new TestLogger<WorkerFunctionMetadataProvider>();
            logger.ClearLogMessages();

            var workerConfigs = TestHelpers.GetTestWorkerConfigs().ToImmutableArray();
            workerConfigs.ToList().ForEach(config => config.Description.WorkerIndexing = "true");

            var scriptApplicationHostOptions = new ScriptApplicationHostOptions();
            var optionsMonitor = TestHelpers.CreateOptionsMonitor(scriptApplicationHostOptions);

            var mockScriptHostManager = new Mock<IScriptHostManager>();
            mockScriptHostManager.Setup(m => m.State).Returns(ScriptHostState.Running);

            var mockWebHostRpcWorkerChannelManager = new Mock<IWebHostRpcWorkerChannelManager>();
            mockWebHostRpcWorkerChannelManager.Setup(m => m.GetChannels(It.IsAny<string>())).Returns(() => new Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>>
            {
            });

            var environment = SystemEnvironment.Instance;
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");

            var workerFunctionMetadataProvider = new WorkerFunctionMetadataProvider(optionsMonitor, logger, SystemEnvironment.Instance,
                                                    mockWebHostRpcWorkerChannelManager.Object, mockScriptHostManager.Object);
            await workerFunctionMetadataProvider.GetFunctionMetadataAsync(workerConfigs, false);

            var traces = logger.GetLogMessages();

            // Assert that the logs contain the expected messages
            Assert.Equal(3, traces.Count);
            Assert.Equal("Fetching metadata for workerRuntime: node", traces[0].FormattedMessage);
            Assert.Equal("Reading functions metadata (Worker)", traces[1].FormattedMessage);
            // The third log is Host is running without any initialized channels, restarting the JobHost. This is not relevant to this test.
        }

        [Fact]
        public void ValidateFunctionMetadata_IsoStringNotAltered()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();
            var isoString = "2025-02-10T22:45:33Z";
            rawBindings.Add("{\"type\": \"cosmosDBTrigger\",\"name\": \"cosmosTrigger\",\"direction\": \"in\",\"databaseName\":\"databaseName\"," +
                "\"containerName\":\"containerNameFoo\",\"leaseContainerName\":\"leaseContanerFoo\",\"createLeaseContainerIfNotExists\":true," +
                "\"connection\":\"CosmosConnection\",\"startFromTime\":\"" + isoString + "\",\"dataType\":\"String\"}");

            var function = _workerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            Assert.Equal(isoString, function.Bindings.FirstOrDefault().Raw["startFromTime"].ToString());
        }
    }
}