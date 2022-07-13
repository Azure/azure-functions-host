//// Copyright (c) .NET Foundation. All rights reserved.
//// Licensed under the MIT License. See License.txt in the project root for license information.

//using MSOptions = Microsoft.Extensions.Options;
//using WorkerHarness.Core.GrpcService;
//using WorkerHarness.Core.Options;
//using System.Text.Json.Nodes;
//using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
//using WorkerHarness.Core.Commons;

//namespace WorkerHarness.Core.Tests.GrpcService
//{
//    [TestClass]
//    public class GrpcMessageProviderTests
//    {
//        [TestMethod]
//        public void Create_NullContent_ThrowArgumentException()
//        {
//            // Arrange
//            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
//            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);

//            StreamingMessageProvider provider = new(stubOptions);

//            string messageType = "WorkerInitRequest";
//            string expectedExceptionMessage = string.Format(StreamingMessageProvider.NullPayloadMessage, messageType);

//            // Act
//            try
//            {
//                provider.Create("WorkerInitRequest", null);
//            }
//            // Assert
//            catch(ArgumentException ex)
//            {
//                StringAssert.Contains(ex.Message, expectedExceptionMessage);
//                return;
//            }

//            Assert.Fail($"The expected {typeof(ArgumentException)} is not thronw");
//        }

//        [TestMethod]
//        [DataRow("")]
//        [DataRow("NewMessageType")]
//        public void Create_NotSupportedMessageType_ThrowArgumentException(string messageType)
//        {
//            // Arrange
//            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
//            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);

//            StreamingMessageProvider provider = new(stubOptions);

//            JsonNode stubNode = JsonNode.Parse("{}")!;

//            string expectedExceptionMessage = string.Format(StreamingMessageProvider.UnsupportedMessageType, messageType);

//            // Act
//            try
//            {
//                provider.Create(messageType, stubNode);
//            }
//            // Assert
//            catch (ArgumentException ex)
//            {
//                StringAssert.Contains(ex.Message, expectedExceptionMessage);
//                return;
//            }

//            Assert.Fail($"The expected {typeof(ArgumentException)} is not thronw");
//        }

//        [TestMethod]
//        public void CreateWorkerInitRequest_EmptyPayload_MessagePopulatedWithHarnessOptions()
//        {
//            // Arrange
//            string input = @"{}";
//            JsonNode payload = JsonNode.Parse(input)!;
//            string messageType = "WorkerInitRequest";

//            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
//            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
//            StreamingMessageProvider provider = new(stubOptions);

//            // Act
//            StreamingMessage message = provider.Create(messageType, payload);

//            // Assert
//            Assert.IsNotNull(message.WorkerInitRequest);
//            Assert.AreEqual(harnessOptions.WorkerDirectory, message.WorkerInitRequest.WorkerDirectory);
//            Assert.AreEqual(harnessOptions.WorkerDirectory, message.WorkerInitRequest.FunctionAppDirectory);
//            Assert.AreEqual(HostConstants.HostVersion, message.WorkerInitRequest.HostVersion);
//            Assert.IsTrue(message.WorkerInitRequest.Capabilities.Count == 0);
//            Assert.IsTrue(message.WorkerInitRequest.LogCategories.Count == 0);
//        }

//        [TestMethod]
//        public void CreateWorkerInitRequest_ValidPayload_MessagePopulatedWithPayload()
//        {
//            // Arrange
//            string input = @"{
//                ""HostVersion"": ""1.8.5"",
//                ""Capabilities"": { ""WorkerIndexing"": ""true"" },
//                ""LogCategories"": { ""Functions.MyFunction"": ""Information"" },
//                ""WorkerDirectory"": ""\\random\\path\\to\\worker\\directory"",
//                ""FunctionAppDirectory"": ""\\random\\path\\to\\function\\app\\directory""
//            }";
//            JsonNode payload = JsonNode.Parse(input)!;
//            string messageType = "WorkerInitRequest";

//            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
//            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
//            StreamingMessageProvider provider = new(stubOptions);

//            // Act
//            StreamingMessage message = provider.Create(messageType, payload);

//            // Assert
//            Assert.IsNotNull(message.WorkerInitRequest);
//            Assert.AreEqual(@"\random\path\to\worker\directory", message.WorkerInitRequest.WorkerDirectory);
//            Assert.AreEqual(@"\random\path\to\function\app\directory", message.WorkerInitRequest.FunctionAppDirectory);
//            Assert.AreEqual("1.8.5", message.WorkerInitRequest.HostVersion);
//            Assert.IsTrue(message.WorkerInitRequest.Capabilities.Contains(new KeyValuePair<string, string>("WorkerIndexing", "true")));
//            Assert.IsTrue(message.WorkerInitRequest.LogCategories.Contains(new KeyValuePair<string, RpcLog.Types.Level>("Functions.MyFunction", RpcLog.Types.Level.Information)));
//        }

//        [TestMethod]
//        public void CreateFunctionLoadRequest_ValidPayload_MessagePopulatedWithPayload()
//        {
//            // Arrange
//            string input = @"{
//              ""FunctionId"": ""function1"",
//              ""Metadata"": {
//                ""ScriptFile"": ""FunctionApp1.dll"",
//                ""EntryPoint"": ""FunctionApp1.SimpleQueueTrigger.Run"",
//                ""Name"": ""SimpleQueueTrigger"",
//                ""Bindings"": {
//                    ""myQueueItem"": {
//                        ""type"": ""queueTrigger"",
//                        ""dataType"": ""string""
//                    }
//                }
//              }   
//            }";
//            JsonNode payload = JsonNode.Parse(input)!;
//            string messageType = "FunctionLoadRequest";

//            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
//            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
//            StreamingMessageProvider provider = new(stubOptions);

//            // Act
//            StreamingMessage message = provider.Create(messageType, payload);

//            // Assert
//            Assert.IsNotNull(message.FunctionLoadRequest);
//            Assert.AreEqual("function1", message.FunctionLoadRequest.FunctionId);
//            Assert.IsNotNull(message.FunctionLoadRequest.Metadata);
//            Assert.AreEqual("FunctionApp1.dll", message.FunctionLoadRequest.Metadata.ScriptFile);
//            Assert.AreEqual("FunctionApp1.SimpleQueueTrigger.Run", message.FunctionLoadRequest.Metadata.EntryPoint);
//            Assert.AreEqual("SimpleQueueTrigger", message.FunctionLoadRequest.Metadata.Name);
//            Assert.IsNotNull(message.FunctionLoadRequest.Metadata.Bindings);
//            Assert.IsTrue(message.FunctionLoadRequest.Metadata.Bindings.Contains(
//                new KeyValuePair<string, BindingInfo>("myQueueItem", new BindingInfo() { Type = "queueTrigger", DataType = BindingInfo.Types.DataType.String })));
//            Assert.IsTrue(message.FunctionLoadRequest.Metadata.RawBindings.Count == 0);
//        }

//        [TestMethod]
//        public void CreateInvocationRequest_ValidPayload_MessagePopulatedWithPayload()
//        {
//            // Arrange
//            string input = @"{
//              ""FunctionId"": ""function1"",
//              ""InputData"": [
//                  {
//                    ""name"": ""myQueueItem"",
//                    ""data"": { ""string"": ""Hello, there!"" }
//                  }
//              ],
//              ""TraceContext"": { }
//            }";
//            JsonNode payload = JsonNode.Parse(input)!;
//            string messageType = "InvocationRequest";

//            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
//            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
//            StreamingMessageProvider provider = new(stubOptions);

//            // Act
//            StreamingMessage message = provider.Create(messageType, payload);

//            // Assert
//            Assert.IsNotNull(message.InvocationRequest);
//            Assert.AreEqual("function1", message.InvocationRequest.FunctionId);
//            Assert.IsTrue(message.InvocationRequest.InputData.Count == 1);
//            Assert.AreEqual("myQueueItem", message.InvocationRequest.InputData[0].Name);
//            Assert.AreEqual("Hello, there!", message.InvocationRequest.InputData[0].Data.String);
//            Assert.IsTrue(message.InvocationRequest.TriggerMetadata.Count == 0);
//            Assert.IsTrue(message.InvocationRequest.TraceContext.Attributes.Count == 0);
//        }

//        [TestMethod]
//        public void CreateFunctionsMetadataRequest_ValidPayLoad_MessagePopulatedWithPayload()
//        {
//            // Arrange
//            string input = @"{ ""FunctionAppDirectory"": ""path\\to\\function\\app\\directory"" }";
//            JsonNode payload = JsonNode.Parse(input)!;
//            string messageType = "FunctionsMetadataRequest";

//            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
//            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
//            StreamingMessageProvider provider = new(stubOptions);

//            // Act
//            StreamingMessage message = provider.Create(messageType, payload);

//            // Assert
//            Assert.IsNotNull(message.FunctionsMetadataRequest);
//            Assert.AreEqual("path\\to\\function\\app\\directory", message.FunctionsMetadataRequest.FunctionAppDirectory);
//        }

//        [TestMethod]
//        public void CreateFunctionsMetadataRequest_EmptyPayload_MessagePopulatedWithHarnessOptions()
//        {
//            // Arrange
//            string input = @"{}";
//            JsonNode payload = JsonNode.Parse(input)!;
//            string messageType = "FunctionsMetadataRequest";

//            HarnessOptions harnessOptions = CreateDefaultHarnessOptions();
//            MSOptions.IOptions<HarnessOptions> stubOptions = MSOptions.Options.Create<HarnessOptions>(harnessOptions);
//            StreamingMessageProvider provider = new(stubOptions);

//            // Act
//            StreamingMessage message = provider.Create(messageType, payload);

//            // Assert
//            Assert.IsNotNull(message.FunctionsMetadataRequest);
//            Assert.AreEqual(harnessOptions.WorkerDirectory, message.FunctionsMetadataRequest.FunctionAppDirectory);
//        }

//        [TestMethod]
//        public void CreateFunctionLoadRequestCollection_ValidPayload_MessagePopulatedWithPayload()
//        {
//        }

//        private static HarnessOptions CreateDefaultHarnessOptions()
//        {
//            return new HarnessOptions() 
//            {
//                ScenarioFile = "path\\to\\scenario\\path",
//                LanguageExecutable = "path\\to\\language\\executable",
//                WorkerExecutable = "path\\to\\worker\\executable",
//                WorkerDirectory = "path\\to\\worker\\directory"
//            };
//        }
//    }
//}
