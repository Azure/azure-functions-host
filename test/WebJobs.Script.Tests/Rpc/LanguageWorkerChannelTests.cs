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
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class LanguageWorkerChannelTests
    {
        private static string _expectedLogMsg = "Outbound event subscribe event handler invoked";

        private Mock<ILanguageWorkerProcess> _mockLanguageWorkerProcess = new Mock<ILanguageWorkerProcess>();
        private string _workerId = "testWorkerId";
        private string _scriptRootPath = "c:\testdir";
        private IScriptEventManager _eventManager = new ScriptEventManager();
        private Mock<IMetricsLogger> _mockMetricsLogger = new Mock<IMetricsLogger>();
        private Mock<ILanguageWorkerConsoleLogSource> _mockConsoleLogger = new Mock<ILanguageWorkerConsoleLogSource>();
        private Mock<FunctionRpc.FunctionRpcBase> _mockFunctionRpcService = new Mock<FunctionRpc.FunctionRpcBase>();
        private TestRpcServer _testRpcServer = new TestRpcServer();
        private ILoggerFactory _loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
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

            _workerChannel = new LanguageWorkerChannel(
               _workerId,
               _scriptRootPath,
               _eventManager,
               testWorkerConfig,
               _mockLanguageWorkerProcess.Object,
               _loggerFactory,
               _mockMetricsLogger.Object,
               0);
            _workerChannel.WorkerChannelLogger = _logger;
        }

        [Fact]
        public async Task StartWorkerProcessAsync_Invoked()
        {
            await _workerChannel.StartWorkerProcessAsync();
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
