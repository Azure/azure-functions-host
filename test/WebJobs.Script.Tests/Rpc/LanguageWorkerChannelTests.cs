﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
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
        private static string _expectedSystemLogMessage = "Random system log message";
        private static string _expectedLoadMsgPartial = "Sending FunctionLoadRequest for ";

        private Mock<ILanguageWorkerProcess> _mockLanguageWorkerProcess = new Mock<ILanguageWorkerProcess>();
        private string _workerId = "testWorkerId";
        private string _scriptRootPath = "c:\testdir";
        private IScriptEventManager _eventManager = new ScriptEventManager();
        private TestMetricsLogger _metricsLogger = new TestMetricsLogger();
        private Mock<ILanguageWorkerConsoleLogSource> _mockConsoleLogger = new Mock<ILanguageWorkerConsoleLogSource>();
        private Mock<FunctionRpc.FunctionRpcBase> _mockFunctionRpcService = new Mock<FunctionRpc.FunctionRpcBase>();
        private TestRpcServer _testRpcServer = new TestRpcServer();
        private ILoggerFactory _loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
        private TestFunctionRpcService _testFunctionRpcService;
        private TestLogger _logger;
        private LanguageWorkerChannel _workerChannel;
        private IEnumerable<FunctionMetadata> _functions = new List<FunctionMetadata>();
        private WorkerConfig _testWorkerConfig;

        public LanguageWorkerChannelTests()
        {
            _logger = new TestLogger("FunctionDispatcherTests");
            _testFunctionRpcService = new TestFunctionRpcService(_eventManager, _workerId, _logger, _expectedLogMsg);
            _testWorkerConfig = TestHelpers.GetTestWorkerConfigs().FirstOrDefault();
            _mockLanguageWorkerProcess.Setup(m => m.StartProcessAsync()).Returns(Task.CompletedTask);

            _workerChannel = new LanguageWorkerChannel(
               _workerId,
               _scriptRootPath,
               _eventManager,
               _testWorkerConfig,
               _mockLanguageWorkerProcess.Object,
               _logger,
               _metricsLogger,
               0);
        }

        [Fact]
        public async Task StartWorkerProcessAsync_Invoked()
        {
            var initTask = _workerChannel.StartWorkerProcessAsync();
            _testFunctionRpcService.PublishStartStreamEvent(_workerId);
            _testFunctionRpcService.PublishWorkerInitResponseEvent();
            await initTask;
            _mockLanguageWorkerProcess.Verify(m => m.StartProcessAsync(), Times.Once);
        }

        [Fact]
        public async Task StartWorkerProcessAsync_TimesOut()
        {
            var initTask = _workerChannel.StartWorkerProcessAsync();
            await Assert.ThrowsAsync<TimeoutException>(async () => await initTask);
        }

        [Fact]
        public async Task SendEnvironmentReloadRequest_Generates_ExpectedMetrics()
        {
            _metricsLogger.ClearCollections();
            Task waitForMetricsTask = Task.Factory.StartNew(() =>
            {
                while (!_metricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationEnvironmentReloadRequestResponse))
                {
                }
            });
            Task reloadRequestResponse = _workerChannel.SendFunctionEnvironmentReloadRequest().ContinueWith(t => { });
            await Task.WhenAny(reloadRequestResponse, waitForMetricsTask, Task.Delay(5000));
            Assert.True(_metricsLogger.EventsBegan.Contains(MetricEventNames.SpecializationEnvironmentReloadRequestResponse));
        }

        [Fact]
        public async Task StartWorkerProcessAsync_WorkerProcess_Throws()
        {
            Mock<ILanguageWorkerProcess> mockLanguageWorkerProcessThatThrows = new Mock<ILanguageWorkerProcess>();
            mockLanguageWorkerProcessThatThrows.Setup(m => m.StartProcessAsync()).Throws<FileNotFoundException>();

            _workerChannel = new LanguageWorkerChannel(
               _workerId,
               _scriptRootPath,
               _eventManager,
               _testWorkerConfig,
               mockLanguageWorkerProcessThatThrows.Object,
               _logger,
               _metricsLogger,
               0);
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await _workerChannel.StartWorkerProcessAsync());
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

        [Theory]
        [InlineData(RpcLog.Types.Level.Information, RpcLog.Types.Level.Information)]
        [InlineData(RpcLog.Types.Level.Error, RpcLog.Types.Level.Error)]
        [InlineData(RpcLog.Types.Level.Warning, RpcLog.Types.Level.Warning)]
        [InlineData(RpcLog.Types.Level.Trace, RpcLog.Types.Level.Information)]
        public void SendSystemLogMessage_PublishesSystemLogMessage(RpcLog.Types.Level levelToTest, RpcLog.Types.Level expectedLogLevel)
        {
            _testFunctionRpcService.PublishSystemLogEvent(levelToTest);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, _expectedSystemLogMessage) && m.Level.ToString().Equals(expectedLogLevel.ToString())));
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
            _metricsLogger.ClearCollections();
            _workerChannel.SetupFunctionInvocationBuffers(GetTestFunctionsList("node"));
            _workerChannel.SendFunctionLoadRequests();
            var traces = _logger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, _expectedLogMsg));
            AreExpectedMetricsGenerated();
            Assert.True(functionLoadLogs.Count() == 2);
        }

        [Fact]
        public void SendLoadRequests_PublishesOutboundEvents_OrdersDisabled()
        {
            var funcName = "ADisabledFunc";
            var functions = GetTestFunctionsList_WithDisabled("node", funcName);

            // Make sure disabled func is input as first
            Assert.True(functions.First().Name == funcName);

            _workerChannel.SetupFunctionInvocationBuffers(functions);
            _workerChannel.SendFunctionLoadRequests();
            var traces = _logger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => m.FormattedMessage?.Contains(_expectedLoadMsgPartial) ?? false);
            var t = functionLoadLogs.Last<LogMessage>().FormattedMessage;

            // Make sure that disabled func shows up last
            Assert.True(functionLoadLogs.Last<LogMessage>().FormattedMessage.Contains(funcName));
            Assert.False(functionLoadLogs.First<LogMessage>().FormattedMessage.Contains(funcName));
            Assert.True(functionLoadLogs.Count() == 3);
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
        public async Task SendSendFunctionEnvironmentReloadRequest_ThrowsTimeout()
        {
            var reloadTask = _workerChannel.SendFunctionEnvironmentReloadRequest();
            await Assert.ThrowsAsync<TimeoutException>(async () => await reloadTask);
        }

        [Fact]
        public void SendSendFunctionEnvironmentReloadRequest_SanitizedEnvironmentVariables()
        {
            var environmentVariables = new Dictionary<string, string>()
            {
                { "TestNull", null },
                { "TestEmpty", string.Empty },
                { "TestValid", "TestValue" }
            };

            FunctionEnvironmentReloadRequest envReloadRequest = _workerChannel.GetFunctionEnvironmentReloadRequest(environmentVariables);
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

        private IEnumerable<FunctionMetadata> GetTestFunctionsList_WithDisabled(string runtime, string funcName)
        {
            var disabledList = new List<FunctionMetadata>()
            {
                new FunctionMetadata()
                {
                    Language = runtime,
                    Name = funcName,
                    FunctionId = "DisabledFunctionId1",
                    IsDisabled = true
                }
            };

            return disabledList.Union(GetTestFunctionsList(runtime));
        }

        private bool AreExpectedMetricsGenerated()
        {
            return _metricsLogger.EventsBegan.Contains(MetricEventNames.FunctionLoadRequestResponse);
        }
    }
}