// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using MSOptions = Microsoft.Extensions.Options;
using WorkerHarness.Core.Options;
using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using WorkerHarness.Core.Commons;
using WorkerHarness.Core.Variables;
using Moq;
using WorkerHarness.Core.StreamingMessageService;

namespace WorkerHarness.Core.Tests.StreamingMessageService
{
    [TestClass]
    public class StreamingMessageProviderTests
    {
        [TestMethod]
        public void Create_NullContent_ThrowArgumentException()
        {
            // Arrange
            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            string messageType = "WorkerInitRequest";
            string expectedExceptionMessage = string.Format(StreamingMessageProvider.NullPayloadMessage, messageType);

            // Act
            try
            {
                provider.Create("WorkerInitRequest", null);
            }
            // Assert
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, expectedExceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} is not thronw");
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("NewMessageType")]
        public void Create_NotSupportedMessageType_ThrowArgumentException(string messageType)
        {
            // Arrange
            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            JsonNode stubNode = JsonNode.Parse("{}")!;

            string expectedExceptionMessage = string.Format(StreamingMessageProvider.UnsupportedMessageType, messageType);

            // Act
            try
            {
                provider.Create(messageType, stubNode);
            }
            // Assert
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, expectedExceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} is not thronw");
        }

        [TestMethod]
        public void CreateWorkerInitRequest_EmptyPayload_MessagePopulatedWithHarnessOptions()
        {
            // Arrange
            string input = @"{}";
            JsonNode payload = JsonNode.Parse(input)!;
            string messageType = "WorkerInitRequest";

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            StreamingMessage message = provider.Create(messageType, payload);

            // Assert
            Assert.IsNotNull(message.WorkerInitRequest);
            Assert.AreEqual(harnessOptions.WorkerDirectory, message.WorkerInitRequest.WorkerDirectory);
            Assert.AreEqual(harnessOptions.WorkerDirectory, message.WorkerInitRequest.FunctionAppDirectory);
            Assert.AreEqual(HostConstants.HostVersion, message.WorkerInitRequest.HostVersion);
            Assert.IsTrue(message.WorkerInitRequest.Capabilities.Count == 0);
            Assert.IsTrue(message.WorkerInitRequest.LogCategories.Count == 0);
        }

        [TestMethod]
        public void CreateWorkerInitRequest_ValidPayload_MessagePopulatedWithPayload()
        {
            // Arrange
            string input = @"{
                ""HostVersion"": ""1.8.5"",
                ""Capabilities"": { ""WorkerIndexing"": ""true"" },
                ""LogCategories"": { ""Functions.MyFunction"": ""Information"" },
                ""WorkerDirectory"": ""\\random\\path\\to\\worker\\directory"",
                ""FunctionAppDirectory"": ""\\random\\path\\to\\function\\app\\directory""
            }";
            JsonNode payload = JsonNode.Parse(input)!;
            string messageType = "WorkerInitRequest";

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            StreamingMessage message = provider.Create(messageType, payload);

            // Assert
            Assert.IsNotNull(message.WorkerInitRequest);
            Assert.AreEqual(@"\random\path\to\worker\directory", message.WorkerInitRequest.WorkerDirectory);
            Assert.AreEqual(@"\random\path\to\function\app\directory", message.WorkerInitRequest.FunctionAppDirectory);
            Assert.AreEqual("1.8.5", message.WorkerInitRequest.HostVersion);
            Assert.IsTrue(message.WorkerInitRequest.Capabilities.Contains(new KeyValuePair<string, string>("WorkerIndexing", "true")));
            Assert.IsTrue(message.WorkerInitRequest.LogCategories.Contains(new KeyValuePair<string, RpcLog.Types.Level>("Functions.MyFunction", RpcLog.Types.Level.Information)));
        }

        [TestMethod]
        public void CreateFunctionLoadRequest_ValidPayload_MessagePopulatedWithPayload()
        {
            // Arrange
            string input = @"{
              ""FunctionId"": ""function1"",
              ""Metadata"": {
                ""ScriptFile"": ""FunctionApp1.dll"",
                ""EntryPoint"": ""FunctionApp1.SimpleQueueTrigger.Run"",
                ""Name"": ""SimpleQueueTrigger"",
                ""Bindings"": {
                    ""myQueueItem"": {
                        ""type"": ""queueTrigger"",
                        ""dataType"": ""string""
                    }
                }
              }   
            }";
            JsonNode payload = JsonNode.Parse(input)!;
            string messageType = "FunctionLoadRequest";

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            StreamingMessage message = provider.Create(messageType, payload);

            // Assert
            Assert.IsNotNull(message.FunctionLoadRequest);
            Assert.AreEqual("function1", message.FunctionLoadRequest.FunctionId);
            Assert.IsNotNull(message.FunctionLoadRequest.Metadata);
            Assert.AreEqual("FunctionApp1.dll", message.FunctionLoadRequest.Metadata.ScriptFile);
            Assert.AreEqual("FunctionApp1.SimpleQueueTrigger.Run", message.FunctionLoadRequest.Metadata.EntryPoint);
            Assert.AreEqual("SimpleQueueTrigger", message.FunctionLoadRequest.Metadata.Name);
            Assert.IsNotNull(message.FunctionLoadRequest.Metadata.Bindings);
            Assert.IsTrue(message.FunctionLoadRequest.Metadata.Bindings.Contains(
                new KeyValuePair<string, BindingInfo>("myQueueItem", new BindingInfo() { Type = "queueTrigger", DataType = BindingInfo.Types.DataType.String })));
            Assert.IsTrue(message.FunctionLoadRequest.Metadata.RawBindings.Count == 0);
        }

        [TestMethod]
        public void CreateInvocationRequest_ValidPayload_MessagePopulatedWithPayload()
        {
            // Arrange
            string input = @"{
              ""FunctionId"": ""function1"",
              ""InputData"": [
                  {
                    ""name"": ""myQueueItem"",
                    ""data"": { ""string"": ""Hello, there!"" }
                  }
              ],
              ""TraceContext"": { }
            }";
            JsonNode payload = JsonNode.Parse(input)!;
            string messageType = "InvocationRequest";

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            StreamingMessage message = provider.Create(messageType, payload);

            // Assert
            Assert.IsNotNull(message.InvocationRequest);
            Assert.AreEqual("function1", message.InvocationRequest.FunctionId);
            Assert.IsTrue(message.InvocationRequest.InputData.Count == 1);
            Assert.AreEqual("myQueueItem", message.InvocationRequest.InputData[0].Name);
            Assert.AreEqual("Hello, there!", message.InvocationRequest.InputData[0].Data.String);
            Assert.IsTrue(message.InvocationRequest.TriggerMetadata.Count == 0);
            Assert.IsTrue(message.InvocationRequest.TraceContext.Attributes.Count == 0);
        }

        [TestMethod]
        public void CreateFunctionsMetadataRequest_ValidPayLoad_MessagePopulatedWithPayload()
        {
            // Arrange
            string input = @"{ ""FunctionAppDirectory"": ""path\\to\\function\\app\\directory"" }";
            JsonNode payload = JsonNode.Parse(input)!;
            string messageType = "FunctionsMetadataRequest";

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            StreamingMessage message = provider.Create(messageType, payload);

            // Assert
            Assert.IsNotNull(message.FunctionsMetadataRequest);
            Assert.AreEqual("path\\to\\function\\app\\directory", message.FunctionsMetadataRequest.FunctionAppDirectory);
        }

        [TestMethod]
        public void CreateFunctionsMetadataRequest_EmptyPayload_MessagePopulatedWithHarnessOptions()
        {
            // Arrange
            string input = @"{}";
            JsonNode payload = JsonNode.Parse(input)!;
            string messageType = "FunctionsMetadataRequest";

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            StreamingMessage message = provider.Create(messageType, payload);

            // Assert
            Assert.IsNotNull(message.FunctionsMetadataRequest);
            Assert.AreEqual(harnessOptions.WorkerDirectory, message.FunctionsMetadataRequest.FunctionAppDirectory);
        }

        [TestMethod]
        public void CreateFunctionLoadRequestCollection_ValidPayload_MessagePopulatedWithPayload()
        {
            // Arrange
            string messageType = "FunctionLoadRequestCollection";
            JsonNode payload = new JsonObject
            {
                ["FunctionLoadRequests"] = new JsonArray(
                    new JsonObject
                    {
                        ["FunctionId"] = "function1",
                        ["Metadata"] = new JsonObject
                        {
                            ["ScriptFile"] = "FunctionApp1.dll",
                            ["EntryPoint"] = "FunctionApp1.SimpleQueueTrigger.Run",
                            ["Name"] = "SimpleQueueTrigger",
                            ["Bindings"] = new JsonObject
                            {
                                ["myQueueItem"] = new JsonObject
                                {
                                    ["type"] = "queueTrigger",
                                    ["dataType"] = "string"
                                }
                            }
                        }
                    }
                ),
            };

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            StreamingMessage message = provider.Create(messageType, payload);

            // Assert
            Assert.AreEqual(StreamingMessage.ContentOneofCase.FunctionLoadRequestCollection, message.ContentCase);
            Assert.IsNotNull(message.FunctionLoadRequestCollection);
            Assert.IsNotNull(message.FunctionLoadRequestCollection.FunctionLoadRequests);
            Assert.AreEqual(1, message.FunctionLoadRequestCollection.FunctionLoadRequests.Count);
            Assert.IsTrue(message.FunctionLoadRequestCollection.FunctionLoadRequests[0] is FunctionLoadRequest);
        }

        [TestMethod]
        public void CreateWorkerTerminate_ValidPayload_MessagePopulatedWithPayload()
        {
            // Arranges
            string messageType = "WorkerTerminate";
            JsonNode payload = new JsonObject
            {
                ["GracePeriod"] = new JsonObject
                {
                    ["Seconds"] = 5
                }
            };

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            StreamingMessage message = provider.Create(messageType, payload);

            // Assert
            Assert.IsNotNull(message.WorkerTerminate);
            Assert.IsNotNull(message.WorkerTerminate.GracePeriod);
            Assert.AreEqual(5, message.WorkerTerminate.GracePeriod.Seconds);
        }

        [TestMethod]
        public void CreateFileChangeEventRequest_ValidPayload_MessagePopulatedWithPayload()
        {
            // Arrange
            string messageType = "FileChangeEventRequest";
            JsonNode payload = new JsonObject
            {
                ["Type"] = "Created",
                ["FullPath"] = "path\\to\\new\\file",
                ["Name"] = "FileName"
            };

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            StreamingMessage message = provider.Create(messageType, payload);

            // Assert
            Assert.IsNotNull(message.FileChangeEventRequest);
            Assert.AreEqual(FileChangeEventRequest.Types.Type.Created, message.FileChangeEventRequest.Type);
            Assert.AreEqual("path\\to\\new\\file", message.FileChangeEventRequest.FullPath);
            Assert.AreEqual("FileName", message.FileChangeEventRequest.Name);
        }

        [TestMethod]
        public void CreateInvocationCancel_ValidPayload_MessagePopulatedWithPayload()
        {
            // Arrange
            string messageType = "InvocationCancel";
            JsonNode payload = new JsonObject
            {
                ["InvocationId"] = "123",
                ["GracePeriod"] = new JsonObject
                {
                    ["Seconds"] = 5
                }
            };

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            StreamingMessage message = provider.Create(messageType, payload);

            // Assert
            Assert.IsNotNull(message.InvocationCancel);
            Assert.AreEqual("123", message.InvocationCancel.InvocationId);
            Assert.IsNotNull(message.InvocationCancel.GracePeriod);
            Assert.AreEqual(5, message.InvocationCancel.GracePeriod.Seconds);
        }

        [TestMethod]
        public void CreateFunctionEnvironmentReloadRequest_ValidPayload_MessagePopulatedWithPayload()
        {
            // Arrange
            string messageType = "FunctionEnvironmentReloadRequest";
            JsonNode payload = new JsonObject
            {
                ["EnvironmentVariables"] = new JsonObject
                {
                    ["var1"] = "hello",
                    ["var2"] = "summer"
                },
                ["FunctionAppDirectory"] = "path\\to\\function\\app"
            };

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            StreamingMessage message = provider.Create(messageType, payload);

            // Assert
            Assert.IsNotNull(message.FunctionEnvironmentReloadRequest);
            Assert.AreEqual("path\\to\\function\\app", message.FunctionEnvironmentReloadRequest.FunctionAppDirectory);
            Assert.AreEqual(2, message.FunctionEnvironmentReloadRequest.EnvironmentVariables.Count);
            Assert.IsTrue(message.FunctionEnvironmentReloadRequest.EnvironmentVariables.Contains(new KeyValuePair<string, string>("var1", "hello")));
            Assert.IsTrue(message.FunctionEnvironmentReloadRequest.EnvironmentVariables.Contains(new KeyValuePair<string, string>("var2", "summer")));
        }

        [TestMethod]
        public void CreateCloseSharedMemoryResourcesRequest_ValidPayload_MessagePopulatedWithPayload()
        {
            // Arrange
            string messageType = "CloseSharedMemoryResourcesRequest";
            JsonNode payload = new JsonObject
            {
                ["MapNames"] = new JsonArray("var1", "var2")
            };

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            StreamingMessage message = provider.Create(messageType, payload);

            // Assert
            Assert.IsNotNull(message.CloseSharedMemoryResourcesRequest);
            Assert.IsNotNull(message.CloseSharedMemoryResourcesRequest.MapNames);
            Assert.AreEqual(2, message.CloseSharedMemoryResourcesRequest.MapNames.Count);
            Assert.IsTrue(message.CloseSharedMemoryResourcesRequest.MapNames.Contains("var1"));
            Assert.IsTrue(message.CloseSharedMemoryResourcesRequest.MapNames.Contains("var2"));
        }

        [TestMethod]
        public void TryCreate_NullPayload_ThrowArgumentException()
        {
            // Arrange
            string messageType = "WorkerInitRequest";
            JsonNode? payload = null;

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
            IPayloadVariableSolver stubPayloadVariableSolver = new PayloadVariableSolver();

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            try
            {
                provider.TryCreate(out StreamingMessage message, messageType, payload, new VariableManager());
            }
            // Assert
            catch (ArgumentException ex)
            {
                string expectedMessage = string.Format(StreamingMessageProvider.NullPayloadMessage, messageType);
                StringAssert.Contains(ex.Message, expectedMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} is not thrown");
        }

        [TestMethod]
        public void TryCreate_TrySolveVariablesReturnTrue_ReturnTrue()
        {
            // Arrange
            string messageType = "FunctionsMetadataRequest";
            JsonNode payload = new JsonObject
            {
                ["FunctionAppDirectory"] = "path\\to\\functions\\app"
            };

            IVariableObservable variableObservable = new VariableManager();

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);

            var mockPayloadVariableSolver = new Mock<IPayloadVariableSolver>();
            mockPayloadVariableSolver
                .Setup(x => x.TrySolveVariables(out payload, payload, variableObservable))
                .Returns(true);
            IPayloadVariableSolver stubPayloadVariableSolver = mockPayloadVariableSolver.Object;

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            bool actual = provider.TryCreate(out StreamingMessage message, messageType, payload, variableObservable);

            // Assert
            Assert.IsTrue(actual);
            Assert.IsNotNull(message.FunctionsMetadataRequest);
            Assert.AreEqual("path\\to\\functions\\app", message.FunctionsMetadataRequest.FunctionAppDirectory);
        }

        [TestMethod]
        public void TryCreate_TrySolveVariablesReturnFalse_ReturnFalse()
        {
            // Arrange
            string messageType = "FunctionsMetadataRequest";
            JsonNode payload = new JsonObject();

            IVariableObservable variableObservable = new VariableManager();

            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);

            var mockPayloadVariableSolver = new Mock<IPayloadVariableSolver>();
            mockPayloadVariableSolver
                .Setup(x => x.TrySolveVariables(out payload, payload, variableObservable))
                .Returns(false);
            IPayloadVariableSolver stubPayloadVariableSolver = mockPayloadVariableSolver.Object;

            StreamingMessageProvider provider = new(stubOptions, stubPayloadVariableSolver);

            // Act
            bool actual = provider.TryCreate(out StreamingMessage message, messageType, payload, variableObservable);

            // Assert
            Assert.IsFalse(actual);
            Assert.IsNull(message.FunctionsMetadataRequest);
            Assert.AreEqual(StreamingMessage.ContentOneofCase.None, message.ContentCase);
        }

        private static HarnessOptions CreateDefaultHarnessOptions()
        {
            return new HarnessOptions()
            {
                ScenarioFile = "path\\to\\scenario\\path",
                LanguageExecutable = "path\\to\\language\\executable",
                WorkerExecutable = "path\\to\\worker\\executable",
                WorkerDirectory = "path\\to\\worker\\directory"
            };
        }
    }
}
