﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.FunctionDataCache;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class GrpcWorkerChannelTests : IDisposable
    {
        private static string _expectedLogMsg = "Outbound event subscribe event handler invoked";
        private static string _expectedSystemLogMessage = "Random system log message";
        private static string _expectedLoadMsgPartial = "Sending FunctionLoadRequest for ";

        private readonly Mock<IWorkerProcess> _mockrpcWorkerProcess = new Mock<IWorkerProcess>();
        private readonly string _workerId = "testWorkerId";
        private readonly string _scriptRootPath = "c:\testdir";
        private readonly IScriptEventManager _eventManager = new ScriptEventManager();
        private readonly Mock<IScriptEventManager> _eventManagerMock = new Mock<IScriptEventManager>();
        private readonly TestMetricsLogger _metricsLogger = new TestMetricsLogger();
        private readonly Mock<IWorkerConsoleLogSource> _mockConsoleLogger = new Mock<IWorkerConsoleLogSource>();
        private readonly Mock<FunctionRpc.FunctionRpcBase> _mockFunctionRpcService = new Mock<FunctionRpc.FunctionRpcBase>();
        private readonly TestRpcServer _testRpcServer = new TestRpcServer();
        private readonly ILoggerFactory _loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
        private readonly TestFunctionRpcService _testFunctionRpcService;
        private readonly TestLogger _logger;
        private readonly IEnumerable<FunctionMetadata> _functions = new List<FunctionMetadata>();
        private readonly RpcWorkerConfig _testWorkerConfig;
        private readonly TestEnvironment _testEnvironment;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _hostOptionsMonitor;
        private readonly IMemoryMappedFileAccessor _mapAccessor;
        private readonly ISharedMemoryManager _sharedMemoryManager;
        private readonly IFunctionDataCache _functionDataCache;
        private readonly IOptions<WorkerConcurrencyOptions> _workerConcurrencyOptions;
        private readonly ITestOutputHelper _testOutput;
        private GrpcWorkerChannel _workerChannel;
        private GrpcWorkerChannel _workerChannelwithMockEventManager;

        public GrpcWorkerChannelTests(ITestOutputHelper testOutput)
        {
            _eventManager.AddGrpcChannels(_workerId);
            _testOutput = testOutput;
            _logger = new TestLogger("FunctionDispatcherTests", testOutput);
            _testFunctionRpcService = new TestFunctionRpcService(_eventManager, _workerId, _logger, _expectedLogMsg);
            _testWorkerConfig = TestHelpers.GetTestWorkerConfigs().FirstOrDefault();
            _testWorkerConfig.CountOptions.ProcessStartupTimeout = TimeSpan.FromSeconds(5);
            _testWorkerConfig.CountOptions.InitializationTimeout = TimeSpan.FromSeconds(5);
            _testWorkerConfig.CountOptions.EnvironmentReloadTimeout = TimeSpan.FromSeconds(5);

            _mockrpcWorkerProcess.Setup(m => m.StartProcessAsync()).Returns(Task.CompletedTask);
            _mockrpcWorkerProcess.Setup(m => m.Id).Returns(910);
            _testEnvironment = new TestEnvironment();
            _testEnvironment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, "1");
            _workerConcurrencyOptions = Options.Create(new WorkerConcurrencyOptions());
            _workerConcurrencyOptions.Value.CheckInterval = TimeSpan.FromSeconds(1);

            ILogger<MemoryMappedFileAccessor> mmapAccessorLogger = NullLogger<MemoryMappedFileAccessor>.Instance;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _mapAccessor = new MemoryMappedFileAccessorWindows(mmapAccessorLogger);
            }
            else
            {
                _mapAccessor = new MemoryMappedFileAccessorUnix(mmapAccessorLogger, _testEnvironment);
            }
            _sharedMemoryManager = new SharedMemoryManager(_loggerFactory, _mapAccessor);
            _functionDataCache = new FunctionDataCache(_sharedMemoryManager, _loggerFactory, _testEnvironment);

            var hostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = _scriptRootPath,
                LogPath = Environment.CurrentDirectory, // not tested
                SecretsPath = Environment.CurrentDirectory, // not tested
                HasParentScope = true
            };
            _hostOptionsMonitor = TestHelpers.CreateOptionsMonitor(hostOptions);

            _testEnvironment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ENABLE_AGENT", "true");
        }

        private Task CreateDefaultWorkerChannel(bool autoStart = true, IDictionary<string, string> capabilities = null, bool mockEventManager = false)
        {
            if (mockEventManager)
            {
                _eventManagerMock.Setup(proxy => proxy.Publish(It.IsAny<OutboundGrpcEvent>())).Verifiable();
                _workerChannelwithMockEventManager = new GrpcWorkerChannel(
                   _workerId,
                   _eventManagerMock.Object,
                   _testWorkerConfig,
                   _mockrpcWorkerProcess.Object,
                   _logger,
                   _metricsLogger,
                   0,
                   _testEnvironment,
                   _hostOptionsMonitor,
                   _sharedMemoryManager,
                   _functionDataCache,
                   _workerConcurrencyOptions);
            }
            else
            {
                _workerChannel = new GrpcWorkerChannel(
               _workerId,
               _eventManager,
               _testWorkerConfig,
               _mockrpcWorkerProcess.Object,
               _logger,
               _metricsLogger,
               0,
               _testEnvironment,
               _hostOptionsMonitor,
               _sharedMemoryManager,
               _functionDataCache,
               _workerConcurrencyOptions);
            }

            if (autoStart)
            {
                // for most tests, we want things to be responsive to inbound messages
                _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.WorkerInitRequest,
                    () => _testFunctionRpcService.PublishWorkerInitResponseEvent(capabilities));
                return (_workerChannel ?? _workerChannelwithMockEventManager).StartWorkerProcessAsync(CancellationToken.None);
            }
            else
            {
                return Task.CompletedTask;
            }
        }

        private void ShowOutput(string message)
            => _testOutput?.WriteLine(message);

        private void ShowOutput(IList<LogMessage> messages)
        {
            if (_testOutput is not null && messages is not null)
            {
                foreach (var msg in messages)
                {
                    _testOutput.WriteLine(msg.FormattedMessage);
                }
            }
        }

        public void Dispose()
        {
            _sharedMemoryManager.Dispose();
        }

        [Fact]
        public async Task StartWorkerProcessAsync_ThrowsTaskCanceledException_IfDisposed()
        {
            var initTask = CreateDefaultWorkerChannel();

            _workerChannel.Dispose();
            await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            {
                await initTask;
            });
        }

        [Fact]
        public async Task StartWorkerProcessAsync_Invoked_SetupFunctionBuffers_Verify_ReadyForInvocation()
        {
            await CreateDefaultWorkerChannel();
            _mockrpcWorkerProcess.Verify(m => m.StartProcessAsync(), Times.Once);
            Assert.False(_workerChannel.IsChannelReadyForInvocations());
            _workerChannel.SetupFunctionInvocationBuffers(GetTestFunctionsList("node"));
            Assert.True(_workerChannel.IsChannelReadyForInvocations());
        }

        [Fact]
        public async Task DisposingChannel_NotReadyForInvocation()
        {
            try
            {
                await CreateDefaultWorkerChannel();
                Assert.False(_workerChannel.IsChannelReadyForInvocations());
                _workerChannel.SetupFunctionInvocationBuffers(GetTestFunctionsList("node"));
                Assert.True(_workerChannel.IsChannelReadyForInvocations());
                _workerChannel.Dispose();
                Assert.False(_workerChannel.IsChannelReadyForInvocations());
            }
            finally
            {
                var traces = _logger.GetLogMessages();
                ShowOutput(traces);
            }
        }

        [Fact]
        public void SetupFunctionBuffers_Verify_ReadyForInvocation_Returns_False()
        {
            CreateDefaultWorkerChannel();
            Assert.False(_workerChannel.IsChannelReadyForInvocations());
            _workerChannel.SetupFunctionInvocationBuffers(GetTestFunctionsList("node"));
            Assert.False(_workerChannel.IsChannelReadyForInvocations());
        }

        [Fact]
        public async Task StartWorkerProcessAsync_TimesOut()
        {
            await CreateDefaultWorkerChannel(autoStart: false); // suppress for timeout
            var initTask = _workerChannel.StartWorkerProcessAsync(CancellationToken.None);
            await Assert.ThrowsAsync<TimeoutException>(async () => await initTask);
        }

        [Fact]
        public async Task SendEnvironmentReloadRequest_Generates_ExpectedMetrics()
        {
            await CreateDefaultWorkerChannel();
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
            // note: uses custom worker channel
            Mock<IWorkerProcess> mockrpcWorkerProcessThatThrows = new Mock<IWorkerProcess>();
            mockrpcWorkerProcessThatThrows.Setup(m => m.StartProcessAsync()).Throws<FileNotFoundException>();

            _workerChannel = new GrpcWorkerChannel(
               _workerId,
               _eventManager,
               _testWorkerConfig,
               mockrpcWorkerProcessThatThrows.Object,
               _logger,
               _metricsLogger,
               0,
               _testEnvironment,
               _hostOptionsMonitor,
               _sharedMemoryManager,
               _functionDataCache,
               _workerConcurrencyOptions);
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await _workerChannel.StartWorkerProcessAsync(CancellationToken.None));
        }

        [Fact]
        public async Task SendWorkerInitRequest_PublishesOutboundEvent()
        {
            await CreateDefaultWorkerChannel(autoStart: false); // we'll do it manually here
            StartStream startStream = new StartStream()
            {
                WorkerId = _workerId
            };
            StreamingMessage startStreamMessage = new StreamingMessage()
            {
                StartStream = startStream
            };
            GrpcEvent rpcEvent = new GrpcEvent(_workerId, startStreamMessage);
            _testFunctionRpcService.AutoReply(StreamingMessage.ContentOneofCase.WorkerInitRequest);
            _workerChannel.SendWorkerInitRequest(rpcEvent);
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, _expectedLogMsg)));
        }

        [Fact]
        public void WorkerInitRequest_Expected()
        {
            CreateDefaultWorkerChannel(autoStart: false); // doesn't actually need to run; just not be null
            WorkerInitRequest initRequest = _workerChannel.GetWorkerInitRequest();
            Assert.NotNull(initRequest.WorkerDirectory);
            Assert.NotNull(initRequest.FunctionAppDirectory);
            Assert.NotNull(initRequest.HostVersion);
            Assert.Equal("testDir", initRequest.WorkerDirectory);
            Assert.Equal(_scriptRootPath, initRequest.FunctionAppDirectory);
            Assert.Equal(ScriptHost.Version, initRequest.HostVersion);
        }

        [Fact]
        public async Task SendWorkerInitRequest_PublishesOutboundEvent_V2Compatable()
        {
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey, "true");
            await CreateDefaultWorkerChannel();
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, _expectedLogMsg)));
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Worker and host running in V2 compatibility mode")));
        }

        [Theory]
        [InlineData(RpcLog.Types.Level.Information, RpcLog.Types.Level.Information)]
        [InlineData(RpcLog.Types.Level.Error, RpcLog.Types.Level.Error)]
        [InlineData(RpcLog.Types.Level.Warning, RpcLog.Types.Level.Warning)]
        [InlineData(RpcLog.Types.Level.Trace, RpcLog.Types.Level.Information)]
        public async Task SendSystemLogMessage_PublishesSystemLogMessage(RpcLog.Types.Level levelToTest, RpcLog.Types.Level expectedLogLevel)
        {
            await CreateDefaultWorkerChannel();
            _testFunctionRpcService.PublishSystemLogEvent(levelToTest);
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, _expectedSystemLogMessage) && m.Level.ToString().Equals(expectedLogLevel.ToString())));
        }

        [Fact]
        public async Task SendInvocationRequest_PublishesOutboundEvent()
        {
            await CreateDefaultWorkerChannel();
            ScriptInvocationContext scriptInvocationContext = GetTestScriptInvocationContext(Guid.NewGuid(), null);
            await _workerChannel.SendInvocationRequest(scriptInvocationContext);
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, _expectedLogMsg)));
        }

        [Fact]
        public async Task SendInvocationRequest_IsInExecutingInvocation()
        {
            await CreateDefaultWorkerChannel();
            ScriptInvocationContext scriptInvocationContext = GetTestScriptInvocationContext(Guid.NewGuid(), null);
            await _workerChannel.SendInvocationRequest(scriptInvocationContext);
            Assert.True(_workerChannel.IsExecutingInvocation(scriptInvocationContext.ExecutionContext.InvocationId.ToString()));
            Assert.False(_workerChannel.IsExecutingInvocation(Guid.NewGuid().ToString()));
        }

        /// <summary>
        /// Verify that <see cref="ScriptInvocationContext"/> with <see cref="RpcSharedMemory"/> input can be sent.
        /// </summary>
        [Fact]
        public async Task SendInvocationRequest_InputsTransferredOverSharedMemory()
        {
            await CreateSharedMemoryEnabledWorkerChannel();

            // Send invocation which will be using RpcSharedMemory for the inputs
            ScriptInvocationContext scriptInvocationContext = GetTestScriptInvocationContextWithSharedMemoryInputs(Guid.NewGuid(), null);
            await _workerChannel.SendInvocationRequest(scriptInvocationContext);
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, _expectedLogMsg)));
        }

        [Fact]
        public async Task Drain_Verify()
        {
            // note: uses custom worker channel
            var resultSource = new TaskCompletionSource<ScriptInvocationResult>();
            Guid invocationId = Guid.NewGuid();
            GrpcWorkerChannel channel = new GrpcWorkerChannel(
               _workerId,
               _eventManager,
               _testWorkerConfig,
               _mockrpcWorkerProcess.Object,
               _logger,
               _metricsLogger,
               0,
               _testEnvironment,
               _hostOptionsMonitor,
               _sharedMemoryManager,
               _functionDataCache,
               _workerConcurrencyOptions);
            channel.SetupFunctionInvocationBuffers(GetTestFunctionsList("node"));
            ScriptInvocationContext scriptInvocationContext = GetTestScriptInvocationContext(invocationId, resultSource);
            await channel.SendInvocationRequest(scriptInvocationContext);
            Task result = channel.DrainInvocationsAsync();
            Assert.NotEqual(result.Status, TaskStatus.RanToCompletion);
            await channel.InvokeResponse(new InvocationResponse
            {
                InvocationId = invocationId.ToString(),
                Result = new StatusResult
                {
                    Status = StatusResult.Types.Status.Success
                },
            });
            await result;
            Assert.Equal(result.Status, TaskStatus.RanToCompletion);
        }

        [Fact]
        public async Task InFlight_Functions_FailedWithException()
        {
            await CreateDefaultWorkerChannel();
            var resultSource = new TaskCompletionSource<ScriptInvocationResult>();
            ScriptInvocationContext scriptInvocationContext = GetTestScriptInvocationContext(Guid.NewGuid(), resultSource);
            await _workerChannel.SendInvocationRequest(scriptInvocationContext);
            Assert.True(_workerChannel.IsExecutingInvocation(scriptInvocationContext.ExecutionContext.InvocationId.ToString()));
            Exception workerException = new Exception("worker failed");
            _workerChannel.TryFailExecutions(workerException);
            Assert.False(_workerChannel.IsExecutingInvocation(scriptInvocationContext.ExecutionContext.InvocationId.ToString()));
            Assert.Equal(TaskStatus.Faulted, resultSource.Task.Status);
            Assert.Equal(workerException, resultSource.Task.Exception.InnerException);
        }

        [Fact]
        public async Task SendLoadRequests_PublishesOutboundEvents()
        {
            await CreateDefaultWorkerChannel();
            _metricsLogger.ClearCollections();
            _workerChannel.SetupFunctionInvocationBuffers(GetTestFunctionsList("node"));
            _workerChannel.SendFunctionLoadRequests(null, TimeSpan.FromMinutes(5));
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, _expectedLogMsg));
            AreExpectedMetricsGenerated();
            Assert.Equal(3, functionLoadLogs.Count()); // one WorkInitRequest, two FunctionLoadRequest
        }

        [Fact]
        public async Task SendLoadRequestCollection_PublishesOutboundEvents()
        {
            await CreateDefaultWorkerChannel(capabilities: new Dictionary<string, string>() { { RpcWorkerConstants.SupportsLoadResponseCollection, "true" } });

            StartStream startStream = new StartStream()
            {
                WorkerId = _workerId
            };
            StreamingMessage startStreamMessage = new StreamingMessage()
            {
                StartStream = startStream
            };
            GrpcEvent rpcEvent = new GrpcEvent(_workerId, startStreamMessage);
            _workerChannel.SendWorkerInitRequest(rpcEvent);
            _testFunctionRpcService.PublishWorkerInitResponseEvent(new Dictionary<string, string>() { { RpcWorkerConstants.SupportsLoadResponseCollection, "true" } });

            _metricsLogger.ClearCollections();
            IEnumerable<FunctionMetadata> functionMetadata = GetTestFunctionsList("node");
            _workerChannel.SetupFunctionInvocationBuffers(functionMetadata);
            _workerChannel.SendFunctionLoadRequests(null, TimeSpan.FromMinutes(5));
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            ShowOutput(traces);
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, _expectedLogMsg));
            AreExpectedMetricsGenerated();
            Assert.Equal(3, functionLoadLogs.Count());
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, string.Format("Sending FunctionLoadRequestCollection with number of functions:'{0}'", functionMetadata.ToList().Count))));
        }

        [Fact]
        public async Task SendLoadRequests_PublishesOutboundEvents_OrdersDisabled()
        {
            await CreateDefaultWorkerChannel();
            var funcName = "ADisabledFunc";
            var functions = GetTestFunctionsList_WithDisabled("node", funcName);

            // Make sure disabled func is input as first
            Assert.True(functions.First().Name == funcName);

            _workerChannel.SetupFunctionInvocationBuffers(functions);
            _workerChannel.SendFunctionLoadRequests(null, TimeSpan.FromMinutes(5));
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            ShowOutput(traces);
            var functionLoadLogs = traces.Where(m => m.FormattedMessage?.Contains(_expectedLoadMsgPartial) ?? false);
            var t = functionLoadLogs.Last<LogMessage>().FormattedMessage;

            // Make sure that disabled func shows up last
            Assert.True(functionLoadLogs.Last<LogMessage>().FormattedMessage.Contains(funcName));
            Assert.False(functionLoadLogs.First<LogMessage>().FormattedMessage.Contains(funcName));
            Assert.Equal(3, functionLoadLogs.Count());
        }

        [Fact]
        public async Task SendLoadRequests_DoesNotTimeout_FunctionTimeoutNotSet()
        {
            await CreateDefaultWorkerChannel();
            var funcName = "ADisabledFunc";
            var functions = GetTestFunctionsList_WithDisabled("node", funcName);
            _workerChannel.SetupFunctionInvocationBuffers(functions);
            _workerChannel.SendFunctionLoadRequests(null, null);
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            ShowOutput(traces);
            var errorLogs = traces.Where(m => m.Level == LogLevel.Error);
            Assert.Empty(errorLogs);
        }

        [Fact]
        public async Task SendSendFunctionEnvironmentReloadRequest_PublishesOutboundEvents()
        {
            await CreateDefaultWorkerChannel();
            try
            {
                Environment.SetEnvironmentVariable("TestNull", null);
                Environment.SetEnvironmentVariable("TestEmpty", string.Empty);
                Environment.SetEnvironmentVariable("TestValid", "TestValue");
                _testFunctionRpcService.AutoReply(StreamingMessage.ContentOneofCase.FunctionEnvironmentReloadRequest);
                var pending = _workerChannel.SendFunctionEnvironmentReloadRequest();
                await Task.Delay(500);
                await pending; // this can timeout
            }
            catch
            {
                // show what we got even if we fail
                var tmp = _logger.GetLogMessages();
                ShowOutput(tmp);
                throw;
            }
            var traces = _logger.GetLogMessages();
            ShowOutput(traces);
            var functionLoadLogs = traces.Where(m => string.Equals(m.FormattedMessage, "Sending FunctionEnvironmentReloadRequest to WorkerProcess with Pid: '910'"));
            Assert.Equal(1, functionLoadLogs.Count());
        }

        [Fact]
        public async Task SendSendFunctionEnvironmentReloadRequest_ThrowsTimeout()
        {
            await CreateDefaultWorkerChannel();
            var reloadTask = _workerChannel.SendFunctionEnvironmentReloadRequest();
            await Assert.ThrowsAsync<TimeoutException>(async () => await reloadTask);
        }

        [Fact]
        public void SendFunctionEnvironmentReloadRequest_SanitizedEnvironmentVariables()
        {
            CreateDefaultWorkerChannel();
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
            Assert.True(envReloadRequest.EnvironmentVariables.ContainsKey(WorkerConstants.FunctionsWorkerDirectorySettingName));
            Assert.True(envReloadRequest.EnvironmentVariables[WorkerConstants.FunctionsWorkerDirectorySettingName] == "testDir");
        }

        [Fact]
        public void SendFunctionEnvironmentReloadRequest_WithDirectory()
        {
            CreateDefaultWorkerChannel();
            var environmentVariables = new Dictionary<string, string>()
            {
                { "TestValid", "TestValue" }
            };

            FunctionEnvironmentReloadRequest envReloadRequest = _workerChannel.GetFunctionEnvironmentReloadRequest(environmentVariables);
            Assert.True(envReloadRequest.EnvironmentVariables["TestValid"] == "TestValue");
            Assert.True(envReloadRequest.FunctionAppDirectory == _scriptRootPath);
        }

        [Fact]
        public async Task ReceivesInboundEvent_InvocationResponse()
        {
            await CreateDefaultWorkerChannel();
            _testFunctionRpcService.PublishInvocationResponseEvent();
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "InvocationResponse received for invocation id: 'TestInvocationId'")));
        }

        [Fact]
        public async Task ReceivesInboundEvent_FunctionLoadResponse()
        {
            await CreateDefaultWorkerChannel();
            var functionMetadatas = GetTestFunctionsList("node");
            _workerChannel.SetupFunctionInvocationBuffers(functionMetadatas);
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionLoadRequest,
                () => _testFunctionRpcService.PublishFunctionLoadResponseEvent("TestFunctionId1"));
            _workerChannel.SendFunctionLoadRequests(null, TimeSpan.FromMinutes(5));

            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            ShowOutput(traces);

            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Setting up FunctionInvocationBuffer for function: 'js1' with functionId: 'TestFunctionId1'")), "FunctionInvocationBuffer TestFunctionId1");
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Setting up FunctionInvocationBuffer for function: 'js2' with functionId: 'TestFunctionId2'")), "FunctionInvocationBuffer TestFunctionId2");
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Received FunctionLoadResponse for function: 'js1' with functionId: 'TestFunctionId1'.")), "FunctionLoadResponse TestFunctionId1");
        }

        [Fact]
        public async Task ReceivesInboundEvent_Failed_FunctionLoadResponses()
        {
            await CreateDefaultWorkerChannel();
            IDictionary<string, string> capabilities = new Dictionary<string, string>()
            {
                { RpcWorkerConstants.SupportsLoadResponseCollection, "1" }
            };

            StartStream startStream = new StartStream()
            {
                WorkerId = _workerId
            };

            StreamingMessage startStreamMessage = new StreamingMessage()
            {
                StartStream = startStream
            };

            GrpcEvent rpcEvent = new GrpcEvent(_workerId, startStreamMessage);
            _workerChannel.SendWorkerInitRequest(rpcEvent);
            _testFunctionRpcService.PublishWorkerInitResponseEvent(capabilities);

            var functionMetadatas = GetTestFunctionsList("node");
            _workerChannel.SetupFunctionInvocationBuffers(functionMetadatas);
            _workerChannel.SendFunctionLoadRequests(null, TimeSpan.FromMinutes(1));
            _testFunctionRpcService.PublishFunctionLoadResponsesEvent(
                            new List<string>() { "TestFunctionId1", "TestFunctionId2" },
                            new StatusResult() { Status = StatusResult.Types.Status.Failure });

            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Setting up FunctionInvocationBuffer for function: 'js1' with functionId: 'TestFunctionId1'")), "setup TestFunctionId1");
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Setting up FunctionInvocationBuffer for function: 'js2' with functionId: 'TestFunctionId2'")), "setup TestFunctionId2");
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Worker failed to load function: 'js1' with function id: 'TestFunctionId1'.")), "fail TestFunctionId1");
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Worker failed to load function: 'js2' with function id: 'TestFunctionId2'.")), "fail TestFunctionId2");
        }

        [Fact]
        public async Task ReceivesInboundEvent_FunctionLoadResponses()
        {
            await CreateDefaultWorkerChannel();
            IDictionary<string, string> capabilities = new Dictionary<string, string>()
            {
                { RpcWorkerConstants.SupportsLoadResponseCollection, "1" }
            };

            StartStream startStream = new StartStream()
            {
                WorkerId = _workerId
            };

            StreamingMessage startStreamMessage = new StreamingMessage()
            {
                StartStream = startStream
            };

            GrpcEvent rpcEvent = new GrpcEvent(_workerId, startStreamMessage);
            _workerChannel.SendWorkerInitRequest(rpcEvent);
            _testFunctionRpcService.PublishWorkerInitResponseEvent(capabilities);

            var functionMetadatas = GetTestFunctionsList("node");
            _workerChannel.SetupFunctionInvocationBuffers(functionMetadatas);
            _workerChannel.SendFunctionLoadRequests(null, TimeSpan.FromMinutes(1));
            _testFunctionRpcService.PublishFunctionLoadResponsesEvent(
                            new List<string>() { "TestFunctionId1", "TestFunctionId2" },
                            new StatusResult() { Status = StatusResult.Types.Status.Success });

            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Setting up FunctionInvocationBuffer for function: 'js1' with functionId: 'TestFunctionId1'")), "setup TestFunctionId1");
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Setting up FunctionInvocationBuffer for function: 'js2' with functionId: 'TestFunctionId2'")), "setup TestFunctionId2");
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, string.Format("Received FunctionLoadResponseCollection with number of functions: '{0}'.", functionMetadatas.ToList().Count))), "recv FunctionLoadResponseCollection");
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Received FunctionLoadResponse for function: 'js1' with functionId: 'TestFunctionId1'.")), "rev TestFunctionId1");
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Received FunctionLoadResponse for function: 'js2' with functionId: 'TestFunctionId2'.")), "rev TestFunctionId2");
        }

        [Fact]
        public async Task ReceivesInboundEvent_Successful_FunctionMetadataResponse()
        {
            await CreateDefaultWorkerChannel();
            var functionMetadata = GetTestFunctionsList("python");
            var functionId = "id123";
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest,
               () => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, true));
            var functions = _workerChannel.GetFunctionMetadata();

            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            ShowOutput(traces);
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Received the worker function metadata response from worker {_workerChannel.Id}")));
        }

        [Fact]
        public async Task ReceivesInboundEvent_Successful_FunctionMetadataResponse_UseDefaultMetadataIndexing_True()
        {
            await CreateDefaultWorkerChannel();
            var functionMetadata = GetTestFunctionsList("python");
            var functionId = "id123";
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest,
                () => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, true, useDefaultMetadataIndexing: true));
            var functions = _workerChannel.GetFunctionMetadata();

            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            ShowOutput(traces);
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Received the worker function metadata response from worker {_workerChannel.Id}")));
        }

        [Fact]
        public async Task ReceivesInboundEvent_Successful_FunctionMetadataResponse_UseDefaultMetadataIndexing_False()
        {
            await CreateDefaultWorkerChannel();
            var functionMetadata = GetTestFunctionsList("python");
            var functionId = "id123";
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest,
                () => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, true, useDefaultMetadataIndexing: false));
            var functions = _workerChannel.GetFunctionMetadata();

            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            ShowOutput(traces);
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Received the worker function metadata response from worker {_workerChannel.Id}")));
        }

        [Fact]
        public async Task ReceivesInboundEvent_Failed_UseDefaultMetadataIndexing_True_HostIndexing()
        {
            await CreateDefaultWorkerChannel();
            var functionMetadata = GetTestFunctionsList("python");
            var functionId = "id123";
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest,
               () => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, false, useDefaultMetadataIndexing: true));
            var functions = _workerChannel.GetFunctionMetadata();
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            ShowOutput(traces);
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Received the worker function metadata response from worker {_workerChannel.Id}")));
        }

        [Fact]
        public async Task ReceivesInboundEvent_Failed_UseDefaultMetadataIndexing_False_WorkerIndexing()
        {
            await CreateDefaultWorkerChannel();
            var functionMetadata = GetTestFunctionsList("python");
            var functionId = "id123";
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest,
               () => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, false, useDefaultMetadataIndexing: false));
            var functions = _workerChannel.GetFunctionMetadata();
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            ShowOutput(traces);
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Worker failed to index function {functionId}")));
        }

        [Fact]
        public async Task ReceivesInboundEvent_Failed_FunctionMetadataResponse()
        {
            await CreateDefaultWorkerChannel();
            var functionId = "id123";
            var functionMetadata = GetTestFunctionsList("python");
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest,
               () => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, false));
            var functions = _workerChannel.GetFunctionMetadata();
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            ShowOutput(traces);
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Worker failed to index function {functionId}")));
        }

        [Fact]
        public async Task ReceivesInboundEvent_Failed_OverallFunctionMetadataResponse()
        {
            await CreateDefaultWorkerChannel();
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest,
                    () => _testFunctionRpcService.PublishWorkerMetadataResponse("TestFunctionId1", null, null, false, false, false));
            var functions = _workerChannel.GetFunctionMetadata();
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Worker failed to index functions")));
        }

        [Fact]
        public void FunctionLoadRequest_IsExpected()
        {
            CreateDefaultWorkerChannel();
            FunctionMetadata metadata = new FunctionMetadata()
            {
                Language = "node",
                Name = "js1"
            };

            metadata.SetFunctionId("TestFunctionId1");

            var functionLoadRequest = _workerChannel.GetFunctionLoadRequest(metadata, null);
            Assert.False(functionLoadRequest.Metadata.IsProxy);
        }

        /// <summary>
        /// Verify that shared memory data transfer is enabled if all required settings are set.
        /// </summary>
        [Fact]
        public async Task SharedMemoryDataTransferSetting_VerifyEnabled()
        {
            await CreateSharedMemoryEnabledWorkerChannel();
            await Task.Delay(500);
            Assert.True(_workerChannel.IsSharedMemoryDataTransferEnabled(), "shared memory should be enabled");
        }

        /// <summary>
        /// Verify that shared memory data transfer is disabled if none of the required settings have been set.
        /// </summary>
        [Fact]
        public void SharedMemoryDataTransferSetting_VerifyDisabled()
        {
            CreateDefaultWorkerChannel();
            Assert.False(_workerChannel.IsSharedMemoryDataTransferEnabled());
        }

        /// <summary>
        /// Verify that shared memory data transfer is disabled if the worker capability is absent.
        /// All other requirements for shared memory data transfer will be enabled.
        /// </summary>
        [Fact]
        public void SharedMemoryDataTransferSetting_VerifyDisabledIfWorkerCapabilityAbsent()
        {
            // Enable shared memory data transfer in the environment
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerSharedMemoryDataTransferEnabledSettingName, "1");
            CreateDefaultWorkerChannel();

            Assert.False(_workerChannel.IsSharedMemoryDataTransferEnabled());
        }

        /// <summary>
        /// Verify that shared memory data transfer is disabled if the environment variable is absent.
        /// All other requirements for shared memory data transfer will be enabled.
        /// </summary>
        [Fact]
        public void SharedMemoryDataTransferSetting_VerifyDisabledIfEnvironmentVariableAbsent()
        {
            CreateSharedMemoryEnabledWorkerChannel(setEnvironmentVariable: false);
            Assert.False(_workerChannel.IsSharedMemoryDataTransferEnabled());
        }

        [Fact]
        public async Task GetLatencies_StartsTimer_WhenDynamicConcurrencyEnabled()
        {
            // note: uses custom worker channel
            RpcWorkerConfig config = new RpcWorkerConfig()
            {
                Description = new RpcWorkerDescription()
                {
                    Language = RpcWorkerConstants.NodeLanguageWorkerName
                }
            };
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled, "true");
            GrpcWorkerChannel workerChannel = new GrpcWorkerChannel(
               _workerId,
               _eventManager,
               config,
               _mockrpcWorkerProcess.Object,
               _logger,
               _metricsLogger,
               0,
               _testEnvironment,
               _hostOptionsMonitor,
               _sharedMemoryManager,
               _functionDataCache,
               _workerConcurrencyOptions);

            IEnumerable<TimeSpan> latencyHistory = null;

            // wait 10 seconds
            await TestHelpers.Await(() =>
            {
                latencyHistory = workerChannel.GetLatencies();
                return latencyHistory.Count() > 0;
            }, pollingInterval: 1000, timeout: 10 * 1000);

            // We have non empty latencyHistory so the timer was started
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled, null);
        }

        [Fact]
        public async Task GetLatencies_DoesNot_StartTimer_WhenDynamicConcurrencyDisabled()
        {
            // note: uses custom worker channels
            RpcWorkerConfig config = new RpcWorkerConfig()
            {
                Description = new RpcWorkerDescription()
                {
                    Language = RpcWorkerConstants.NodeLanguageWorkerName
                },
            };
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled, null);
            GrpcWorkerChannel workerChannel = new GrpcWorkerChannel(
               _workerId,
               _eventManager,
               config,
               _mockrpcWorkerProcess.Object,
               _logger,
               _metricsLogger,
               0,
               _testEnvironment,
               _hostOptionsMonitor,
               _sharedMemoryManager,
               _functionDataCache,
               _workerConcurrencyOptions);

            // wait 10 seconds
            await Task.Delay(10000);

            IEnumerable<TimeSpan> latencyHistory = workerChannel.GetLatencies();

            Assert.Equal(0, latencyHistory.Count());
        }

        [Fact]
        public async Task SendInvocationRequest_ValidateTraceContext()
        {
            await CreateDefaultWorkerChannel(mockEventManager: true);
            ScriptInvocationContext scriptInvocationContext = GetTestScriptInvocationContext(Guid.NewGuid(), null);
            await _workerChannelwithMockEventManager.SendInvocationRequest(scriptInvocationContext);
            if (_testEnvironment.IsApplicationInsightsAgentEnabled())
            {
                _eventManagerMock.Verify(proxy => proxy.Publish(It.Is<OutboundGrpcEvent>(
                    grpcEvent => grpcEvent.Message.InvocationRequest.TraceContext.Attributes.ContainsKey(ScriptConstants.LogPropertyProcessIdKey)
                    && grpcEvent.Message.InvocationRequest.TraceContext.Attributes.ContainsKey(ScriptConstants.LogPropertyHostInstanceIdKey)
                    && grpcEvent.Message.InvocationRequest.TraceContext.Attributes.ContainsKey(LogConstants.CategoryNameKey)
                    && grpcEvent.Message.InvocationRequest.TraceContext.Attributes[LogConstants.CategoryNameKey].Equals("testcat1")
                    && grpcEvent.Message.InvocationRequest.TraceContext.Attributes.Count == 3)));
            }
            else
            {
                _eventManagerMock.Verify(proxy => proxy.Publish(It.Is<OutboundGrpcEvent>(
                    grpcEvent => !grpcEvent.Message.InvocationRequest.TraceContext.Attributes.ContainsKey(ScriptConstants.LogPropertyProcessIdKey)
                    && !grpcEvent.Message.InvocationRequest.TraceContext.Attributes.ContainsKey(ScriptConstants.LogPropertyHostInstanceIdKey)
                    && !grpcEvent.Message.InvocationRequest.TraceContext.Attributes.ContainsKey(LogConstants.CategoryNameKey))));
            }
        }

        [Fact]
        public async Task SendInvocationRequest_ValidateTraceContext_SessionId()
        {
            await CreateDefaultWorkerChannel(mockEventManager: true);
            string sessionId = "sessionId1234";
            Activity activity = new Activity("testActivity");
            activity.AddBaggage(ScriptConstants.LiveLogsSessionAIKey, sessionId);
            activity.Start();
            ScriptInvocationContext scriptInvocationContext = GetTestScriptInvocationContext(Guid.NewGuid(), null);
            await _workerChannelwithMockEventManager.SendInvocationRequest(scriptInvocationContext);
            activity.Stop();
            _eventManagerMock.Verify(p => p.Publish(It.Is<OutboundGrpcEvent>(grpcEvent => ValidateInvocationRequest(grpcEvent, sessionId))));
        }

        private bool ValidateInvocationRequest(OutboundGrpcEvent grpcEvent, string sessionId)
        {
            if (_testEnvironment.IsApplicationInsightsAgentEnabled())
            {
                return grpcEvent.Message.InvocationRequest.TraceContext.Attributes[ScriptConstants.LiveLogsSessionAIKey].Equals(sessionId)
                && grpcEvent.Message.InvocationRequest.TraceContext.Attributes.ContainsKey(LogConstants.CategoryNameKey)
                && grpcEvent.Message.InvocationRequest.TraceContext.Attributes[LogConstants.CategoryNameKey].Equals("testcat1")
                && grpcEvent.Message.InvocationRequest.TraceContext.Attributes.Count == 4;
            }
            else
            {
                return !grpcEvent.Message.InvocationRequest.TraceContext.Attributes.ContainsKey(LogConstants.CategoryNameKey);
            }
        }

        private IEnumerable<FunctionMetadata> GetTestFunctionsList(string runtime)
        {
            var metadata1 = new FunctionMetadata()
            {
                Language = runtime,
                Name = "js1"
            };

            metadata1.SetFunctionId("TestFunctionId1");
            metadata1.Properties.Add(LogConstants.CategoryNameKey, "testcat1");
            metadata1.Properties.Add(ScriptConstants.LogPropertyHostInstanceIdKey, "testhostId1");
            var metadata2 = new FunctionMetadata()
            {
                Language = runtime,
                Name = "js2",
            };

            metadata2.SetFunctionId("TestFunctionId2");
            metadata2.Properties.Add(LogConstants.CategoryNameKey, "testcat2");
            metadata2.Properties.Add(ScriptConstants.LogPropertyHostInstanceIdKey, "testhostId2");
            return new List<FunctionMetadata>()
            {
                metadata1,
                metadata2
            };
        }

        private ScriptInvocationContext GetTestScriptInvocationContext(Guid invocationId, TaskCompletionSource<ScriptInvocationResult> resultSource)
        {
            return new ScriptInvocationContext()
            {
                FunctionMetadata = GetTestFunctionsList("node").FirstOrDefault(),
                ExecutionContext = new ExecutionContext()
                {
                    InvocationId = invocationId,
                    FunctionName = "js1",
                    FunctionAppDirectory = _scriptRootPath,
                    FunctionDirectory = _scriptRootPath
                },
                BindingData = new Dictionary<string, object>(),
                Inputs = new List<(string name, DataType type, object val)>(),
                ResultSource = resultSource
            };
        }

        /// <summary>
        /// The <see cref="ScriptInvocationContext"/> would contain inputs that can be transferred over shared memory.
        /// </summary>
        /// <param name="invocationId">ID of the invocation.</param>
        /// <param name="resultSource">Task result source.</param>
        /// <returns>A test <see cref="ScriptInvocationContext"/></returns>
        private ScriptInvocationContext GetTestScriptInvocationContextWithSharedMemoryInputs(Guid invocationId, TaskCompletionSource<ScriptInvocationResult> resultSource)
        {
            const int inputStringLength = 2 * 1024 * 1024;
            string inputString = TestUtils.GetRandomString(inputStringLength);

            const int inputBytesLength = 2 * 1024 * 1024;
            byte[] inputBytes = TestUtils.GetRandomBytesInArray(inputBytesLength);

            var inputs = new List<(string name, DataType type, object val)>
            {
                ("fooStr", DataType.String, inputString),
                ("fooBytes", DataType.Binary, inputBytes),
            };

            return new ScriptInvocationContext()
            {
                FunctionMetadata = GetTestFunctionsList("node").FirstOrDefault(),
                ExecutionContext = new ExecutionContext()
                {
                    InvocationId = invocationId,
                    FunctionName = "js1",
                    FunctionAppDirectory = _scriptRootPath,
                    FunctionDirectory = _scriptRootPath
                },
                BindingData = new Dictionary<string, object>(),
                Inputs = inputs,
                ResultSource = resultSource
            };
        }

        private IEnumerable<FunctionMetadata> GetTestFunctionsList_WithDisabled(string runtime, string funcName)
        {
            var metadata = new FunctionMetadata()
            {
                Language = runtime,
                Name = funcName
            };

            metadata.SetFunctionId("DisabledFunctionId1");
            metadata.SetIsDisabled(true);

            var disabledList = new List<FunctionMetadata>()
            {
                metadata
            };

            return disabledList.Union(GetTestFunctionsList(runtime));
        }

        private bool AreExpectedMetricsGenerated()
        {
            return _metricsLogger.EventsBegan.Contains(MetricEventNames.FunctionLoadRequestResponse);
        }

        private Task CreateSharedMemoryEnabledWorkerChannel(bool setEnvironmentVariable = true)
        {
            if (setEnvironmentVariable)
            {
                // Enable shared memory data transfer in the environment
                _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerSharedMemoryDataTransferEnabledSettingName, "1");
            }

            // Enable shared memory data transfer capability in the worker
            IDictionary<string, string> capabilities = new Dictionary<string, string>()
            {
                { RpcWorkerConstants.SharedMemoryDataTransfer, "1" }
            };
            // Send worker init request and enable the capabilities
            return CreateDefaultWorkerChannel(capabilities: capabilities);
        }
    }
}
