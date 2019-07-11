// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.Rpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class LanguageWorkerChannelTests
    {
        private readonly TestLoggerProvider _testLoggerProvider = new TestLoggerProvider();

        private static string _expectedLogMsg = "Outbound event subscribe event handler invoked";

        private Mock<ILanguageWorkerProcess> _mockLanguageWorkerProcess = new Mock<ILanguageWorkerProcess>();
        private string _workerId = "testWorkerId";
        private string _scriptRootPath = "c:\testdir";
        private IScriptEventManager _eventManager = new ScriptEventManager();
        private Mock<IMetricsLogger> _mockMetricsLogger = new Mock<IMetricsLogger>();
        private Mock<FunctionRpc.FunctionRpcBase> _mockFunctionRpcService = new Mock<FunctionRpc.FunctionRpcBase>();
        private TestRpcServer _testRpcServer = new TestRpcServer();
        private TestFunctionRpcService _testFunctionRpcService;
        private TestLogger _logger;
        private LanguageWorkerChannel _workerChannel;
        private IEnumerable<FunctionMetadata> _functions = new List<FunctionMetadata>();

        public LanguageWorkerChannelTests()
        {
            _logger = new TestLogger("FunctionDispatcherTests");
            _testFunctionRpcService = new TestFunctionRpcService(_eventManager, _workerId, _logger, _expectedLogMsg);
            var testWorkerConfig = TestHelpers.GetTestWorkerConfigs().FirstOrDefault();
            _mockLanguageWorkerProcess.Setup(m => m.StartProcess());

            LoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_testLoggerProvider);

            _workerChannel = new LanguageWorkerChannel(
               _workerId,
               _scriptRootPath,
               _eventManager,
               testWorkerConfig,
               _mockLanguageWorkerProcess.Object,
               _logger,
               loggerFactory,
               _mockMetricsLogger.Object,
               0);
        }

        [Fact]
        public async Task StartWorkerProcessAsync_Invoked()
        {
            var initTask = _workerChannel.StartWorkerProcessAsync();
            _testFunctionRpcService.PublishStartStreamEvent(_workerId);
            _testFunctionRpcService.PublishWorkerInitResponseEvent();
            await initTask;
            _mockLanguageWorkerProcess.Verify(m => m.StartProcess(), Times.Once);
        }

        [Fact]
        public void SendWorkerInitRequest_PublishesOutboundEvent()
        {
            StartStream startStream = new StartStream()
            {
                WorkerId = _workerId
            };
            StreamingMessage startStreamMessage = new StreamingMessage()
            {
                StartStream = startStream
            };
            RpcEvent rpcEvent = new RpcEvent(_workerId, startStreamMessage);
            _workerChannel.SendWorkerInitRequest(rpcEvent);
            _testFunctionRpcService.PublishWorkerInitResponseEvent();
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, _expectedLogMsg)));
        }

        [Fact]
        public void SendInvocationRequest_PublishesOutboundEvent()
        {
            ScriptInvocationContext scriptInvocationContext = new ScriptInvocationContext()
            {
                FunctionMetadata = GetTestFunctionsList("node").FirstOrDefault(),
                ExecutionContext = new ExecutionContext()
                {
                    InvocationId = Guid.NewGuid(),
                    FunctionName = "js1",
                    FunctionAppDirectory = _scriptRootPath,
                    FunctionDirectory = _scriptRootPath
                },
                BindingData = new Dictionary<string, object>(),
                Inputs = new List<(string name, DataType type, object val)>()
            };
            _workerChannel.SendInvocationRequest(scriptInvocationContext);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, _expectedLogMsg)));
        }

        [Fact]
        public void SendLoadRequests_PublishesOutboundEvents()
        {
            _workerChannel.SetupFunctionInvocationBuffers(GetTestFunctionsList("node"));
            _workerChannel.SendFunctionLoadRequests();
            var traces = _logger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, _expectedLogMsg));
            Assert.True(functionLoadLogs.Count() == 2);
        }

        [Fact]
        public void SendSendFunctionEnvironmentReloadRequest_PublishesOutboundEvents()
        {
            Environment.SetEnvironmentVariable("TestNull", null);
            Environment.SetEnvironmentVariable("TestEmpty", string.Empty);
            Environment.SetEnvironmentVariable("TestValid", "TestValue");
            _workerChannel.SendFunctionEnvironmentReloadRequest();
            _testFunctionRpcService.PublishFunctionEnvironmentReloadResponseEvent();
            var traces = _logger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "Sending FunctionEnvironmentReloadRequest"));
            Assert.True(functionLoadLogs.Count() == 1);
        }

        [Fact]
        public void SendSendFunctionEnvironmentReloadRequest_SanitizedEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable("TestNull", null);
            Environment.SetEnvironmentVariable("TestEmpty", string.Empty);
            Environment.SetEnvironmentVariable("TestValid", "TestValue");
            FunctionEnvironmentReloadRequest envReloadRequest = _workerChannel.GetFunctionEnvironmentReloadRequest(Environment.GetEnvironmentVariables());
            Assert.False(envReloadRequest.EnvironmentVariables.ContainsKey("TestNull"));
            Assert.False(envReloadRequest.EnvironmentVariables.ContainsKey("TestEmpty"));
            Assert.True(envReloadRequest.EnvironmentVariables.ContainsKey("TestValid"));
            Assert.True(envReloadRequest.EnvironmentVariables["TestValid"] == "TestValue");
        }

        [Fact]
        public void ReceivesInboundEvent_InvocationResponse()
        {
            _testFunctionRpcService.PublishInvocationResponseEvent();
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "InvocationResponse received for invocation id: TestInvocationId")));
        }

        [Fact]
        public void ReceivesInboundEvent_FunctionLoadResponse()
        {
            _workerChannel.SetupFunctionInvocationBuffers(GetTestFunctionsList("node"));
            _testFunctionRpcService.PublishFunctionLoadResponseEvent("TestFunctionId1");
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Setting up FunctionInvocationBuffer for function:js1 with functionId:TestFunctionId1")));
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Setting up FunctionInvocationBuffer for function:js2 with functionId:TestFunctionId2")));
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Received FunctionLoadResponse for functionId:TestFunctionId1")));
        }

        [Fact]
        public void FunctionLoadRequest_IsExpected()
        {
            FunctionMetadata metadata = new FunctionMetadata()
            {
                Language = "node",
                Name = "js1",
                FunctionId = "TestFunctionId1"
            };
            var functionLoadRequest = _workerChannel.GetFunctionLoadRequest(metadata);
            Assert.False(functionLoadRequest.Metadata.IsProxy);

            FunctionMetadata proxyMetadata = new FunctionMetadata()
            {
                Language = "node",
                Name = "js1",
                FunctionId = "TestFunctionId1",
                IsProxy = true
            };
            var proxyFunctionLoadRequest = _workerChannel.GetFunctionLoadRequest(proxyMetadata);
            Assert.True(proxyFunctionLoadRequest.Metadata.IsProxy);
        }

        [Fact]
        public void FunctionLoadResuest_ErrorIsLogged()
        {
            _workerChannel.SetupFunctionInvocationBuffers(GetTestFunctionsList("node"));

            var response = new FunctionLoadResponse
            {
                FunctionId = "TestFunctionId1",
                Result = new StatusResult
                {
                    Exception = new Grpc.Messages.RpcException { Message = "boom!" }
                }
            };

            _workerChannel.LoadResponse(response);

            LogMessage log = _testLoggerProvider.GetAllLogMessages().Single(p => p.Category == "Function.js1");
            Assert.Equal("Function load error.", log.FormattedMessage);
            Assert.IsType<Script.Rpc.RpcException>(log.Exception);
            Assert.Contains("boom!", log.Exception.Message);
        }

        private IEnumerable<FunctionMetadata> GetTestFunctionsList(string runtime)
        {
            return new List<FunctionMetadata>()
            {
                new FunctionMetadata()
                {
                     Language = runtime,
                     Name = "js1",
                     FunctionId = "TestFunctionId1"
                },

                new FunctionMetadata()
                {
                     Language = runtime,
                     Name = "js2",
                     FunctionId = "TestFunctionId2"
                }
            };
        }
    }
}