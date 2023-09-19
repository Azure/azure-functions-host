// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet.ContentModel;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WorkerFunctionMetadataProviderTests
    {
        [Fact]
        public void ValidateBindings_NoBindings_Throws()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();

            var ex = Assert.Throws<FormatException>(() =>
            {
                WorkerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
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
                WorkerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
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
                WorkerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
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
                WorkerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
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
                WorkerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
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
            string scriptPath = Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "node");
            var environment = SystemEnvironment.Instance;
            WorkerFunctionMetadataProvider.ValidateFunctionAppFormat(scriptPath, logger, environment);
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
                WorkerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
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
    }
}