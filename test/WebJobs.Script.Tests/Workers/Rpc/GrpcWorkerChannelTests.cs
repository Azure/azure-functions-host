// Copyright (c) .NET Foundation. All rights reserved.
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
using Microsoft.Azure.WebJobs.Script.Config;
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
        private readonly IOptions<FunctionsHostingConfigOptions> _hostingConfigOptions;
        private readonly Mock<IHttpProxyService> _mockHttpProxyService = new Mock<IHttpProxyService>();
        private readonly IHttpProxyService _httpProxyService;
        private GrpcWorkerChannel _workerChannel;

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

            _hostingConfigOptions = Options.Create(new FunctionsHostingConfigOptions());

            _httpProxyService = _mockHttpProxyService.Object;
        }

        private Task CreateDefaultWorkerChannel(bool autoStart = true, IDictionary<string, string> capabilities = null)
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
               _workerConcurrencyOptions,
               _hostingConfigOptions,
               _httpProxyService);

            if (autoStart)
            {
                // for most tests, we want things to be responsive to inbound messages
                _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.WorkerInitRequest,
                    _ => _testFunctionRpcService.PublishWorkerInitResponseEvent(capabilities));
                return _workerChannel.StartWorkerProcessAsync(CancellationToken.None);
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
        public async Task WorkerChannel_Dispose_With_WorkerTerminateCapability()
        {
            await CreateDefaultWorkerChannel(capabilities: new Dictionary<string, string>() { { RpcWorkerConstants.HandlesWorkerTerminateMessage, "1" } });

            StartStream startStream = new StartStream()
            {
                WorkerId = _workerId
            };

            StreamingMessage startStreamMessage = new StreamingMessage()
            {
                StartStream = startStream
            };

            // Send worker init request and enable the capabilities
            GrpcEvent rpcEvent = new GrpcEvent(_workerId, startStreamMessage);
            _testFunctionRpcService.AutoReply(StreamingMessage.ContentOneofCase.WorkerInitRequest);
            _workerChannel.SendWorkerInitRequest(rpcEvent);

            await Task.Delay(500);

            _workerChannel.Dispose();
            var traces = _logger.GetLogMessages();
            var expectedLogMsg = $"Sending WorkerTerminate message with grace period of {WorkerConstants.WorkerTerminateGracePeriodInSeconds} seconds.";
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, expectedLogMsg)));
        }

        [Fact]
        public async Task WorkerChannel_Dispose_Without_WorkerTerminateCapability()
        {
            await CreateDefaultWorkerChannel();

            _workerChannel.Dispose();
            var traces = _logger.GetLogMessages();
            var expectedLogMsg = $"Sending WorkerTerminate message with grace period of {WorkerConstants.WorkerTerminateGracePeriodInSeconds} seconds.";
            Assert.False(traces.Any(m => string.Equals(m.FormattedMessage, expectedLogMsg)));
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
               _workerConcurrencyOptions,
               _hostingConfigOptions,
               _httpProxyService);
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
            _metricsLogger.ClearCollections();
            ScriptInvocationContext scriptInvocationContext = GetTestScriptInvocationContext(Guid.NewGuid(), null);
            await _workerChannel.SendInvocationRequest(scriptInvocationContext);
            await Task.Delay(500);
            string testWorkerId = _workerId.ToLowerInvariant();
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, _expectedLogMsg)));
            Assert.Equal(1, _metricsLogger.LoggedEvents.Count(e => e.Contains($"{string.Format(MetricEventNames.WorkerInvoked, testWorkerId)}_{scriptInvocationContext.FunctionMetadata.Name}")));
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
        public async Task SendInvocationRequest_SignalCancellation_WithCapability_SendsInvocationCancelRequest()
        {
            var cancellationWaitTimeMs = 3000;
            var invocationId = Guid.NewGuid();
            var expectedCancellationLog = $"Sending InvocationCancel request for invocation: '{invocationId.ToString()}'";

            var cts = new CancellationTokenSource();
            cts.CancelAfter(cancellationWaitTimeMs);
            var token = cts.Token;

            await CreateDefaultWorkerChannel(capabilities: new Dictionary<string, string>() { { RpcWorkerConstants.HandlesInvocationCancelMessage, "true" } });
            var scriptInvocationContext = GetTestScriptInvocationContext(invocationId, null, token);
            await _workerChannel.SendInvocationRequest(scriptInvocationContext);

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(1000);
                if (token.IsCancellationRequested)
                {
                    break;
                }
            }
            await Task.Delay(500);

            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, expectedCancellationLog)));
        }

        [Fact]
        public async Task SendInvocationRequest_SignalCancellation_WithoutCapability_NoAction()
        {
            var cancellationWaitTimeMs = 3000;
            var invocationId = Guid.NewGuid();
            var expectedCancellationLog = $"Sending invocation cancel request for InvocationId {invocationId.ToString()}";

            var cts = new CancellationTokenSource();
            cts.CancelAfter(cancellationWaitTimeMs);
            var token = cts.Token;

            await CreateDefaultWorkerChannel();
            var scriptInvocationContext = GetTestScriptInvocationContext(invocationId, null, token);
            await _workerChannel.SendInvocationRequest(scriptInvocationContext);

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(1000);
                if (token.IsCancellationRequested)
                {
                    break;
                }
            }

            var traces = _logger.GetLogMessages();
            Assert.False(traces.Any(m => string.Equals(m.FormattedMessage, expectedCancellationLog)));
        }

        [Fact]
        public async Task SendInvocationRequest_CancellationAlreadyRequested_ResultSourceCanceled()
        {
            var cancellationWaitTimeMs = 3000;
            var invocationId = Guid.NewGuid();
            var expectedCancellationLog = $"Cancellation has been requested. The invocation request with id '{invocationId}' is canceled and will not be sent to the worker.";

            var cts = new CancellationTokenSource();
            cts.CancelAfter(cancellationWaitTimeMs);
            var token = cts.Token;

            await CreateDefaultWorkerChannel(capabilities: new Dictionary<string, string>() { { RpcWorkerConstants.HandlesInvocationCancelMessage, "true" } });
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(1000);
                if (token.IsCancellationRequested)
                {
                    break;
                }
            }

            var resultSource = new TaskCompletionSource<ScriptInvocationResult>();
            var scriptInvocationContext = GetTestScriptInvocationContext(invocationId, resultSource, token);
            await _workerChannel.SendInvocationRequest(scriptInvocationContext);

            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, expectedCancellationLog)));
            Assert.Equal(TaskStatus.Canceled, resultSource.Task.Status);
        }

        [Fact]
        public async Task SendInvocationCancelRequest_PublishesOutboundEvent()
        {
            var invocationId = Guid.NewGuid();
            var expectedCancellationLog = $"Sending InvocationCancel request for invocation: '{invocationId.ToString()}'";

            await CreateDefaultWorkerChannel(capabilities: new Dictionary<string, string>() { { RpcWorkerConstants.HandlesInvocationCancelMessage, "true" } });
            var scriptInvocationContext = GetTestScriptInvocationContext(invocationId, null);
            _workerChannel.SendInvocationCancel(invocationId.ToString());
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, expectedCancellationLog)));
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, _expectedLogMsg)));
            // The outbound log should happen twice: once for worker init request and once for the invocation cancel request
            Assert.Equal(traces.Where(m => m.FormattedMessage.Equals(_expectedLogMsg)).Count(), 2);
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
               _workerConcurrencyOptions,
               _hostingConfigOptions,
               _httpProxyService);
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
        public async Task SendLoadRequests_SkipParameterBindingData()
        {
            await CreateDefaultWorkerChannel();
            _metricsLogger.ClearCollections();

            var binding = new BindingMetadata()
            {
                Name = "abc",
                Type = "BlobTrigger"
            };

            binding.Properties.Add(ScriptConstants.SkipDeferredBindingKey, true);
            binding.Properties.Add(ScriptConstants.SupportsDeferredBindingKey, true);

            IEnumerable<FunctionMetadata> functionMetadata = GetTestFunctionsList("node");
            foreach (var function in functionMetadata)
            {
                function.Bindings.Add(binding);
            }

            _workerChannel.SetupFunctionInvocationBuffers(functionMetadata);
            _workerChannel.SendFunctionLoadRequests(null, TimeSpan.FromMinutes(5));
            await Task.Delay(500);
            AreExpectedMetricsGenerated();
            Assert.Equal(0, _metricsLogger.LoggedEvents.Count(e => e.Contains(MetricEventNames.FunctionBindingDeferred)));
        }

        [Fact]
        public async Task SendLoadRequests_SupportParameterBindingData()
        {
            await CreateDefaultWorkerChannel();
            _metricsLogger.ClearCollections();

            var binding = new BindingMetadata()
            {
                Name = "abc",
                Type = "BlobTrigger"
            };

            binding.Properties.Add(ScriptConstants.SupportsDeferredBindingKey, true);

            IEnumerable<FunctionMetadata> functionMetadata = GetTestFunctionsList("node");
            foreach (var function in functionMetadata)
            {
                function.Bindings.Add(binding);
            }

            _workerChannel.SetupFunctionInvocationBuffers(functionMetadata);
            _workerChannel.SendFunctionLoadRequests(null, TimeSpan.FromMinutes(5));
            await Task.Delay(500);
            AreExpectedMetricsGenerated();
            Assert.Equal(2, _metricsLogger.LoggedEvents.Count(e => e.Contains(MetricEventNames.FunctionBindingDeferred)));
            Assert.Equal(1, _metricsLogger.LoggedEvents.Count(e => e.Contains($"{MetricEventNames.FunctionBindingDeferred}_js1")));
            Assert.Equal(1, _metricsLogger.LoggedEvents.Count(e => e.Contains($"{MetricEventNames.FunctionBindingDeferred}_js2")));
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
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, string.Format("Sending FunctionLoadRequestCollection with number of functions: '{0}'", functionMetadata.ToList().Count))));
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
        public async Task GetFunctionMetadata_IncludesMetadataProperties()
        {
            await CreateDefaultWorkerChannel();

            var functionMetadata = GetTestFunctionsList("python", true);
            var functionId = "id123";
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest,
               _ => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, successful: true, useDefaultMetadataIndexing: false));

            var functions = await _workerChannel.GetFunctionMetadata();

            Assert.Equal(functions[0].Metadata.Properties.Count, 4);
            Assert.Equal(functions[0].Metadata.Properties["worker.functionId"], "fn1");
        }

        [Fact]
        public async Task SendLoadRequests_IncludesMetadataProperties()
        {
            await CreateDefaultWorkerChannel();

            var functionMetadata = GetTestFunctionsList("python", true);
            var functionId = "id123";
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest,
               _ => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, successful: true, useDefaultMetadataIndexing: false));

            var functions = await _workerChannel.GetFunctionMetadata();

            functionMetadata = functions.Select(f => f.Metadata);
            _workerChannel.SetupFunctionInvocationBuffers(functionMetadata);
            _workerChannel.SendFunctionLoadRequests(null, null);

            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionLoadRequest,
               (m) =>
               {
                   Assert.Contains("\"worker.functionId\": \"fn1\"", m.Message.ToString());
               });

            await Task.Delay(500);
        }

        [Fact]
        public async Task GetFunctionLoadRequest_IncludesAvoidsDuplicateProperties()
        {
            await CreateDefaultWorkerChannel();
            var functionMetadata = GetTestFunctionsList("python");
            var functionId = "TestFunctionId1";
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest,
               _ => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, true));
            var functions = _workerChannel.GetFunctionMetadata();

            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            ShowOutput(traces);
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"FunctionId is already a part of metadata properties for TestFunctionId1")));
        }

        [Fact]
        public async Task GetFunctionLoadRequest_IncludesWorkerProperties()
        {
            await CreateDefaultWorkerChannel();

            var functionMetadata = GetTestFunctionsList("python", true);
            _workerChannel.SetupFunctionInvocationBuffers(functionMetadata);
            var loadRequest = _workerChannel.GetFunctionLoadRequest(functionMetadata.ElementAt(0), null);

            Assert.Equal(loadRequest.Metadata.Properties["worker.functionId"], "fn1");
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
                _testFunctionRpcService.AutoReply(StreamingMessage.ContentOneofCase.FunctionEnvironmentReloadRequest, workerSupportsSpecialization: true);
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

            // for specialization use case, env reload response include worker metadata and capabilities.
            var metatadataLog = traces.Where(m => string.Equals(m.FormattedMessage,
                @"Worker metadata: { ""runtimeName"": "".NET"", ""runtimeVersion"": ""7.0"", ""workerVersion"": ""1.0.0"", ""workerBitness"": ""x64"" }"));
            var capabilityUpdateLog = traces.Where(m => string.Equals(m.FormattedMessage,
                @"Updated capabilities: {""RpcHttpBodyOnly"":""True"",""TypedDataCollection"":""True""}"));
            Assert.Single(metatadataLog);
            Assert.Single(capabilityUpdateLog);
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
        public async Task SendInvocationRequest_PublishesOutboundEvent_ReceivesInvocationResponse()
        {
            await CreateDefaultWorkerChannel();
            _metricsLogger.ClearCollections();
            var invocationId = Guid.NewGuid();
            ScriptInvocationContext scriptInvocationContext = GetTestScriptInvocationContext(invocationId, new TaskCompletionSource<ScriptInvocationResult>());
            await _workerChannel.SendInvocationRequest(scriptInvocationContext);
            _testFunctionRpcService.PublishInvocationResponseEvent(invocationId.ToString());
            await Task.Delay(500);
            var testWorkerId = _workerId.ToLowerInvariant();
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"InvocationResponse received for invocation: '{invocationId}'")));
            Assert.Equal(1, _metricsLogger.LoggedEvents.Count(e => e.Contains($"{string.Format(MetricEventNames.WorkerInvoked, testWorkerId)}_{scriptInvocationContext.FunctionMetadata.Name}")));
            Assert.Equal(1, _metricsLogger.LoggedEvents.Count(e => e.Contains(string.Format(MetricEventNames.WorkerInvokeSucceeded, testWorkerId))));
            Assert.Equal(0, _metricsLogger.LoggedEvents.Count(e => e.Contains(string.Format(MetricEventNames.WorkerInvokeFailed, testWorkerId))));
        }

        [Fact]
        public async Task SendFunctionEnvironmentReloadRequest_AddsHostingConfig()
        {
            _hostingConfigOptions.Value.Features["TestFeature"] = "TestFeatureValue";
            _hostingConfigOptions.Value.Features["TestEnvVariable"] = "TestEnvVariableValue2";

            await CreateDefaultWorkerChannel();

            var environmentVariables = new Dictionary<string, string>()
            {
                { "TestValid", "TestValue" },
                { "TestEnvVariable", "TestEnvVariableValue1" }
            };

            FunctionEnvironmentReloadRequest envReloadRequest = _workerChannel.GetFunctionEnvironmentReloadRequest(environmentVariables);
            Assert.True(envReloadRequest.EnvironmentVariables["TestValid"] == "TestValue");
            Assert.True(envReloadRequest.EnvironmentVariables["TestFeature"] == "TestFeatureValue");
            Assert.True(envReloadRequest.EnvironmentVariables["TestEnvVariable"] == "TestEnvVariableValue2");
        }

        [Fact]
        public async Task ReceivesInboundEvent_InvocationResponse()
        {
            await CreateDefaultWorkerChannel();
            _testFunctionRpcService.PublishInvocationResponseEvent();
            await Task.Delay(500);
            var traces = _logger.GetLogMessages();
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "InvocationResponse received for invocation: 'TestInvocationId'")));
        }

        [Fact]
        public async Task ReceivesInboundEvent_FunctionLoadResponse()
        {
            await CreateDefaultWorkerChannel();
            var functionMetadatas = GetTestFunctionsList("node");
            _workerChannel.SetupFunctionInvocationBuffers(functionMetadatas);
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionLoadRequest,
                _ => _testFunctionRpcService.PublishFunctionLoadResponseEvent("TestFunctionId1"));
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
            IDictionary<string, string> capabilities = new Dictionary<string, string>()
            {
                { RpcWorkerConstants.SupportsLoadResponseCollection, "1" }
            };
            await CreateDefaultWorkerChannel(capabilities: capabilities);

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
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Worker failed to load function: 'js1' with functionId: 'TestFunctionId1'.")), "fail TestFunctionId1");
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, "Worker failed to load function: 'js2' with functionId: 'TestFunctionId2'.")), "fail TestFunctionId2");
        }

        [Fact]
        public async Task ReceivesInboundEvent_FunctionLoadResponses()
        {
            IDictionary<string, string> capabilities = new Dictionary<string, string>()
            {
                { RpcWorkerConstants.SupportsLoadResponseCollection, "1" }
            };
            await CreateDefaultWorkerChannel(capabilities: capabilities);

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
               _ => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, true));
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
                _ => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, true, useDefaultMetadataIndexing: true));
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
                _ => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, true, useDefaultMetadataIndexing: false));
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
               _ => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, false, useDefaultMetadataIndexing: true));
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
               _ => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, false, useDefaultMetadataIndexing: false));
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
               _ => _testFunctionRpcService.PublishWorkerMetadataResponse(_workerId, functionId, functionMetadata, false));
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
                    _ => _testFunctionRpcService.PublishWorkerMetadataResponse("TestFunctionId1", null, null, false, false, false));
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
            ProxyFunctionMetadata proxyMetadata = new ProxyFunctionMetadata(null)
            {
                Language = "node",
                Name = "js1"
            };

            metadata.SetFunctionId("TestFunctionId1");

            var proxyFunctionLoadRequest = _workerChannel.GetFunctionLoadRequest(proxyMetadata, null);
            Assert.True(proxyFunctionLoadRequest.Metadata.IsProxy);
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
               _workerConcurrencyOptions,
               _hostingConfigOptions,
               _httpProxyService);

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
               _workerConcurrencyOptions,
               _hostingConfigOptions,
               _httpProxyService);

            // wait 10 seconds
            await Task.Delay(10000);

            IEnumerable<TimeSpan> latencyHistory = workerChannel.GetLatencies();

            Assert.Equal(0, latencyHistory.Count());
        }

        [Fact]
        public async Task SendInvocationRequest_ValidateTraceContext()
        {
            await CreateDefaultWorkerChannel();
            ScriptInvocationContext scriptInvocationContext = GetTestScriptInvocationContext(Guid.NewGuid(), null);

            await _workerChannel.SendInvocationRequest(scriptInvocationContext);

            RpcTraceContext ctx = null;
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.InvocationRequest, evt =>
            {
                ctx = evt.Message.InvocationRequest.TraceContext;
            });
            await Task.Delay(500);

            Assert.NotNull(ctx);
            var attribs = ctx.Attributes;
            Assert.NotNull(attribs);

            if (_testEnvironment.IsApplicationInsightsAgentEnabled())
            {
                _testOutput.WriteLine("Checking ENABLED app-insights fields...");
                Assert.True(attribs.ContainsKey(ScriptConstants.LogPropertyProcessIdKey), "ScriptConstants.LogPropertyProcessIdKey");
                Assert.True(attribs.ContainsKey(ScriptConstants.LogPropertyHostInstanceIdKey), "ScriptConstants.LogPropertyHostInstanceIdKey");
                Assert.True(attribs.TryGetValue(LogConstants.CategoryNameKey, out var catKey), "LogConstants.CategoryNameKey");
                Assert.Equal(catKey, "testcat1");
                Assert.True(attribs.TryGetValue(ScriptConstants.OperationNameKey, out var onKey), "ScriptConstants.OperationNameKey");
                Assert.Equal(onKey, scriptInvocationContext.ExecutionContext.FunctionName);
                Assert.Equal(4, attribs.Count);
            }
            else
            {
                _testOutput.WriteLine("Checking DISABLED app-insights fields...");
                Assert.False(attribs.ContainsKey(ScriptConstants.LogPropertyProcessIdKey), "ScriptConstants.LogPropertyProcessIdKey");
                Assert.False(attribs.ContainsKey(ScriptConstants.LogPropertyHostInstanceIdKey), "ScriptConstants.LogPropertyHostInstanceIdKey");
                Assert.False(attribs.ContainsKey(LogConstants.CategoryNameKey), "LogConstants.CategoryNameKey");
                Assert.Equal(0, attribs.Count);
            }
        }

        [Fact]
        public async Task SendInvocationRequest_ValidateTraceContext_Properties()
        {
            await CreateDefaultWorkerChannel();
            string sessionId = "sessionId1234";
            Activity activity = new Activity("testActivity");
            activity.AddBaggage(ScriptConstants.LiveLogsSessionAIKey, sessionId);
            activity.Start();
            ScriptInvocationContext scriptInvocationContext = GetTestScriptInvocationContext(Guid.NewGuid(), null);

            OutboundGrpcEvent grpcEvent = null;
            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.InvocationRequest, evt =>
            {
                grpcEvent = evt;
            });
            await _workerChannel.SendInvocationRequest(scriptInvocationContext);
            await Task.Delay(500);

            Assert.NotNull(grpcEvent);

            activity.Stop();
            var attribs = grpcEvent.Message.InvocationRequest.TraceContext.Attributes;

            if (_testEnvironment.IsApplicationInsightsAgentEnabled())
            {
                Assert.True(attribs.TryGetValue(ScriptConstants.LiveLogsSessionAIKey, out var aiKey), "ScriptConstants.LiveLogsSessionAIKey");
                Assert.Equal(sessionId, aiKey);
                Assert.True(attribs.TryGetValue(LogConstants.CategoryNameKey, out var catKey), "LogConstants.CategoryNameKey");
                Assert.Equal("testcat1", catKey);
                Assert.True(attribs.TryGetValue(ScriptConstants.OperationNameKey, out var onKey), "ScriptConstants.OperationNameKey");
                Assert.Equal(scriptInvocationContext.ExecutionContext.FunctionName, onKey);
                Assert.Equal(5, attribs.Count);
            }
            else
            {
                Assert.False(attribs.ContainsKey(LogConstants.CategoryNameKey), "LogConstants.CategoryNameKey");
            }
        }

        [Fact]
        public async Task GetFunctionMetadata_MultipleCalls_ReturnSameTask()
        {
            using var block1 = new SemaphoreSlim(0, 1);
            using var block2 = new SemaphoreSlim(0, 1);
            int count = 0;

            await CreateDefaultWorkerChannel();

            _testFunctionRpcService.OnMessage(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest,
                async _ =>
                {
                    if (Interlocked.Increment(ref count) == 1)
                    {
                        // notify the second request it can start
                        block2.Release();

                        // make the first call sit and wait until we know we've issued the second
                        await block1.WaitAsync();
                    }

                    _testFunctionRpcService.PublishWorkerMetadataResponse("TestFunctionId1", null, null, false, false, false);
                });

            var functionsTask1 = _workerChannel.GetFunctionMetadata();
            await Task.Yield();

            // wait until the first request has made it to the callback before issuing the second
            await block2.WaitAsync();

            var functionsTask2 = _workerChannel.GetFunctionMetadata();
            await Task.Yield();

            // now that both requests have been made, let the first return
            block1.Release();

            //Assert.Same(functionsTask1, functionsTask2);

            var allTask = Task.WhenAll(functionsTask1, functionsTask2);
            var timeoutTask = Task.Delay(5000);

            // the timeout should never fire
            var completedTask = await Task.WhenAny(allTask, timeoutTask);

            Assert.True(completedTask == allTask, "Timed out waiting for tasks to complete");
            Assert.Same(functionsTask1, functionsTask2);
        }

        private IEnumerable<FunctionMetadata> GetTestFunctionsList(string runtime, bool addWorkerProperties = false)
        {
            var metadata1 = new FunctionMetadata()
            {
                Language = runtime,
                Name = "js1"
            };

            metadata1.SetFunctionId("TestFunctionId1");
            metadata1.Properties.Add(LogConstants.CategoryNameKey, "testcat1");
            metadata1.Properties.Add(ScriptConstants.LogPropertyHostInstanceIdKey, "testhostId1");

            if (addWorkerProperties)
            {
                metadata1.Properties.Add("worker.functionId", "fn1");
            }

            var metadata2 = new FunctionMetadata()
            {
                Language = runtime,
                Name = "js2",
            };

            metadata2.SetFunctionId("TestFunctionId2");
            metadata2.Properties.Add(LogConstants.CategoryNameKey, "testcat2");
            metadata2.Properties.Add(ScriptConstants.LogPropertyHostInstanceIdKey, "testhostId2");

            if (addWorkerProperties)
            {
                metadata2.Properties.Add("WORKER.functionId", "fn2");
            }

            return new List<FunctionMetadata>()
            {
                metadata1,
                metadata2
            };
        }

        private ScriptInvocationContext GetTestScriptInvocationContext(Guid invocationId, TaskCompletionSource<ScriptInvocationResult> resultSource, CancellationToken? token = null)
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
                Inputs = new List<(string Name, DataType Type, object Val)>(),
                ResultSource = resultSource,
                CancellationToken = token == null ? CancellationToken.None : (CancellationToken)token
            };
        }

        /// <summary>
        /// The <see cref="ScriptInvocationContext"/> would contain inputs that can be transferred over shared memory.
        /// </summary>
        /// <param name="invocationId">ID of the invocation.</param>
        /// <param name="resultSource">Task result source.</param>
        /// <returns>A test <see cref="ScriptInvocationContext"/>.</returns>
        private ScriptInvocationContext GetTestScriptInvocationContextWithSharedMemoryInputs(Guid invocationId, TaskCompletionSource<ScriptInvocationResult> resultSource)
        {
            const int inputStringLength = 2 * 1024 * 1024;
            string inputString = TestUtils.GetRandomString(inputStringLength);

            const int inputBytesLength = 2 * 1024 * 1024;
            byte[] inputBytes = TestUtils.GetRandomBytesInArray(inputBytesLength);

            var inputs = new List<(string Name, DataType Type, object Val)>
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
