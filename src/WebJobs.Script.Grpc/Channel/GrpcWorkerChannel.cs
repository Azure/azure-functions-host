// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Extensions;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Microsoft.Azure.WebJobs.Script.Grpc.Messages.RpcLog.Types;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;
using MsgType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.StreamingMessage.ContentOneofCase;
using ParameterBindingType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.ParameterBinding.RpcDataOneofCase;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal partial class GrpcWorkerChannel : IRpcWorkerChannel, IDisposable
    {
        private readonly IScriptEventManager _eventManager;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly RpcWorkerConfig _workerConfig;
        private readonly string _runtime;
        private readonly IEnvironment _environment;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly ISharedMemoryManager _sharedMemoryManager;
        private readonly List<TimeSpan> _workerStatusLatencyHistory = new List<TimeSpan>();
        private readonly IOptions<WorkerConcurrencyOptions> _workerConcurrencyOptions;
        private readonly WaitCallback _processInbound;
        private readonly IInvocationMessageDispatcherFactory _messageDispatcherFactory;
        private readonly object _syncLock = new object();
        private readonly object _metadataLock = new object();
        private readonly Dictionary<MsgType, Queue<PendingItem>> _pendingActions = new();
        private readonly ChannelWriter<OutboundGrpcEvent> _outbound;
        private readonly ChannelReader<InboundGrpcEvent> _inbound;
        private readonly IOptions<FunctionsHostingConfigOptions> _hostingConfigOptions;
        private readonly string _workerInvocationSucccededMetric;
        private readonly string _workerInvocationFailedMetric;
        private readonly string _workerId;
        private IDisposable _functionLoadRequestResponseEvent;
        private bool _disposed;
        private bool _disposing;
        private WorkerInitResponse _initMessage;
        private RpcWorkerChannelState _state;
        private IDictionary<string, Exception> _functionLoadErrors = new Dictionary<string, Exception>();
        private IDictionary<string, Exception> _metadataRequestErrors = new Dictionary<string, Exception>();
        private ConcurrentDictionary<string, ExecutingInvocation> _executingInvocations = new();
        private IDictionary<string, BufferBlock<ScriptInvocationContext>> _functionInputBuffers = new ConcurrentDictionary<string, BufferBlock<ScriptInvocationContext>>();
        private ConcurrentDictionary<string, TaskCompletionSource<bool>> _workerStatusRequests = new ConcurrentDictionary<string, TaskCompletionSource<bool>>();
        private ConcurrentBag<IDisposable> _inputLinks = new ConcurrentBag<IDisposable>();
        private List<IDisposable> _eventSubscriptions = new List<IDisposable>();
        private IDisposable _startLatencyMetric;
        private IEnumerable<FunctionMetadata> _functions;
        private GrpcCapabilities _workerCapabilities;
        private ILogger _workerChannelLogger;
        private IMetricsLogger _metricsLogger;
        private IWorkerProcess _rpcWorkerProcess;
        private TaskCompletionSource<bool> _reloadTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> _workerInitTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<List<RawFunctionMetadata>> _functionsIndexingTask = new TaskCompletionSource<List<RawFunctionMetadata>>(TaskCreationOptions.RunContinuationsAsynchronously);
        private TimeSpan _functionLoadTimeout = TimeSpan.FromMinutes(1);
        private bool _isSharedMemoryDataTransferEnabled;
        private bool _isHandlesInvocationCancelMessageCapabilityEnabled;
        private bool _isWorkerApplicationInsightsLoggingEnabled;
        private IHttpProxyService _httpProxyService;
        private Uri _httpProxyEndpoint;
        private System.Timers.Timer _timer;
        private bool _functionMetadataRequestSent = false;
        private IOptions<ScriptJobHostOptions> _scriptHostOptions;

        internal GrpcWorkerChannel(
            string workerId,
            IScriptEventManager eventManager,
            IScriptHostManager hostManager,
            RpcWorkerConfig workerConfig,
            IWorkerProcess rpcWorkerProcess,
            ILogger logger,
            IMetricsLogger metricsLogger,
            int attemptCount,
            IEnvironment environment,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
            ISharedMemoryManager sharedMemoryManager,
            IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions,
            IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
            IHttpProxyService httpProxyService)
        {
            _workerId = workerId;
            _eventManager = eventManager;
            _scriptHostManager = hostManager;
            _workerConfig = workerConfig;
            _runtime = workerConfig.Description.Language;
            _rpcWorkerProcess = rpcWorkerProcess;
            _workerChannelLogger = logger;
            _metricsLogger = metricsLogger;
            _environment = environment;
            _applicationHostOptions = applicationHostOptions;
            _sharedMemoryManager = sharedMemoryManager;
            _workerConcurrencyOptions = workerConcurrencyOptions;
            _processInbound = state => ProcessItem((InboundGrpcEvent)state);
            _hostingConfigOptions = hostingConfigOptions;

            _httpProxyService = httpProxyService;
            _workerCapabilities = new GrpcCapabilities(_workerChannelLogger);

            if (!_eventManager.TryGetGrpcChannels(workerId, out var inbound, out var outbound))
            {
                throw new InvalidOperationException("Could not get gRPC channels for worker ID: " + workerId);
            }

            _outbound = outbound.Writer;
            _inbound = inbound.Reader;
            // note: we don't start the read loop until StartWorkerProcessAsync is called

            _eventSubscriptions.Add(_eventManager.OfType<FileEvent>()
                .Where(msg => _workerConfig.Description.Extensions.Contains(Path.GetExtension(msg.FileChangeArguments.FullPath)))
                .Throttle(TimeSpan.FromMilliseconds(300)) // debounce
                .Subscribe(msg => _eventManager.Publish(new HostRestartEvent())));

            _startLatencyMetric = metricsLogger?.LatencyEvent(string.Format(MetricEventNames.WorkerInitializeLatency, workerConfig.Description.Language, attemptCount));

            _state = RpcWorkerChannelState.Default;

            // Temporary switch to allow fully testing new algorithm in production
            _messageDispatcherFactory = GetProcessorFactory();

            _scriptHostManager.ActiveHostChanged += HandleActiveHostChange;

            // Reused metrics strings
            _workerInvocationSucccededMetric = string.Format(MetricEventNames.WorkerInvokeSucceeded, Id);
            _workerInvocationFailedMetric = string.Format(MetricEventNames.WorkerInvokeFailed, Id);

            LoadScriptJobHostOptions(_scriptHostManager as IServiceProvider);
        }

        private bool IsHttpProxyingWorker => _httpProxyEndpoint is not null;

        public string Id => _workerId;

        public IDictionary<string, BufferBlock<ScriptInvocationContext>> FunctionInputBuffers => _functionInputBuffers;

        public IWorkerProcess WorkerProcess => _rpcWorkerProcess;

        public RpcWorkerConfig WorkerConfig => _workerConfig;

        public IOptions<ScriptJobHostOptions> JobHostOptions => _scriptHostOptions;

        internal bool IsSharedMemoryDataTransferEnabled => _isSharedMemoryDataTransferEnabled;

        private void HandleActiveHostChange(object sender, ActiveHostChangedEventArgs e)
        {
            if (e.NewHost is null)
            {
                return;
            }

            LoadScriptJobHostOptions(e.NewHost.Services);
        }

        private void LoadScriptJobHostOptions(IServiceProvider provider)
        {
            if (provider?.GetService(typeof(IOptions<ScriptJobHostOptions>)) is IOptions<ScriptJobHostOptions> scriptHostOptions)
            {
                _scriptHostOptions = scriptHostOptions;
            }
            else
            {
                _workerChannelLogger.LogDebug("Unable to resolve ScriptJobHostOptions");
            }
        }

        // Temporary switch that allows us to move between the "old" ThreadPool-only processor
        // and a "new" Channel processor (for proper ordering of messages).
        private IInvocationMessageDispatcherFactory GetProcessorFactory()
        {
            // Ordered invocations is the default, but allow explicit disabling
            if (!_hostingConfigOptions.Value.EnableOrderedInvocationMessages ||
                FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableOrderedInvocationMessages, _environment))
            {
                _workerChannelLogger.LogDebug($"Using {nameof(ThreadPoolInvocationProcessorFactory)}.");
                return new ThreadPoolInvocationProcessorFactory(_processInbound);
            }
            else
            {
                _workerChannelLogger.LogDebug($"Using {nameof(OrderedInvocationMessageDispatcherFactory)}.");
                return new OrderedInvocationMessageDispatcherFactory(ProcessItem, _workerChannelLogger);
            }
        }

        private void ProcessItem(InboundGrpcEvent msg)
        {
            // note this method is a thread-pool (QueueUserWorkItem) entry-point
            try
            {
                switch (msg.MessageType)
                {
                    case MsgType.RpcLog when msg.Message.RpcLog.LogCategory == RpcLogCategory.System:
                        SystemLog(msg);
                        break;
                    case MsgType.RpcLog:
                        Log(msg);
                        break;
                    case MsgType.WorkerStatusResponse:
                        ReceiveWorkerStatusResponse(msg.Message.RequestId, msg.Message.WorkerStatusResponse);
                        break;
                    case MsgType.InvocationResponse:
                        _ = InvokeResponse(msg.Message.InvocationResponse);
                        break;
                    default:
                        ProcessRegisteredGrpcCallbacks(msg);
                        break;
                }
            }
            catch (Exception ex)
            {
                _workerChannelLogger.LogError(ex, "Error processing InboundGrpcEvent: " + ex.Message);
            }
        }

        private void ProcessRegisteredGrpcCallbacks(InboundGrpcEvent message)
        {
            Queue<PendingItem> queue;
            lock (_pendingActions)
            {
                if (!_pendingActions.TryGetValue(message.MessageType, out queue))
                {
                    return; // nothing to do
                }
            }
            PendingItem next;
            lock (queue)
            {
                do
                {
                    if (!queue.TryDequeue(out next))
                    {
                        return; // nothing to do
                    }
                }
                while (next.IsComplete);
            }
            next.SetResult(message);
        }

        private void RegisterCallbackForNextGrpcMessage(MsgType messageType, TimeSpan timeout, int count, Action<InboundGrpcEvent> callback, Action<Exception> faultHandler)
        {
            Queue<PendingItem> queue;
            lock (_pendingActions)
            {
                if (!_pendingActions.TryGetValue(messageType, out queue))
                {
                    queue = new Queue<PendingItem>();
                    _pendingActions.Add(messageType, queue);
                }
            }

            lock (queue)
            {
                // while we have the lock, discard any dead items (to prevent unbounded growth on stall)
                while (queue.TryPeek(out var next) && next.IsComplete)
                {
                    queue.Dequeue();
                }
                for (int i = 0; i < count; i++)
                {
                    var newItem = (i == count - 1) && (timeout != TimeSpan.Zero)
                        ? new PendingItem(callback, faultHandler, timeout)
                        : new PendingItem(callback, faultHandler);
                    queue.Enqueue(newItem);
                }
            }
        }

        private async Task ProcessInbound()
        {
            try
            {
                await Task.Yield(); // free up the caller
                bool debug = _workerChannelLogger.IsEnabled(LogLevel.Debug);
                if (debug)
                {
                    _workerChannelLogger.LogDebug("[channel] processing reader loop for worker {0}:", _workerId);
                }
                while (await _inbound.WaitToReadAsync())
                {
                    while (_inbound.TryRead(out var msg))
                    {
                        if (debug && msg.MessageType != MsgType.RpcLog)
                        {
                            Logger.ChannelReceivedMessage(_workerChannelLogger, msg.WorkerId, msg.MessageType);
                        }

                        DispatchMessage(msg);
                    }
                }
            }
            catch (Exception ex)
            {
                _workerChannelLogger.LogError(ex, "Error processing inbound messages");
            }
            finally
            {
                // we're not listening any more! shut down the channels
                _eventManager.RemoveGrpcChannels(_workerId);
            }
        }

        private void DispatchMessage(InboundGrpcEvent msg)
        {
            // RpcLog and InvocationResponse messages are special. They need to be handled by the InvocationMessageDispatcher
            switch (msg.MessageType)
            {
                case MsgType.RpcLog when msg.Message.RpcLog.LogCategory == RpcLogCategory.User || msg.Message.RpcLog.LogCategory == RpcLogCategory.CustomMetric:
                    if (_executingInvocations.TryGetValue(msg.Message.RpcLog.InvocationId, out var invocation))
                    {
                        invocation.Dispatcher.DispatchRpcLog(msg);
                    }
                    else
                    {
                        // We received a log outside of a invocation
                        ThreadPool.QueueUserWorkItem(_processInbound, msg);
                    }
                    break;
                case MsgType.InvocationResponse:
                    if (_executingInvocations.TryGetValue(msg.Message.InvocationResponse.InvocationId, out invocation))
                    {
                        invocation.Dispatcher.DispatchInvocationResponse(msg);
                    }
                    else
                    {
                        // This should never happen, but if it does, just send it to the ThreadPool.
                        ThreadPool.QueueUserWorkItem(_processInbound, msg);
                    }
                    break;
                default:
                    // All other messages can go to the thread pool.
                    ThreadPool.QueueUserWorkItem(_processInbound, msg);
                    break;
            }
        }

        public bool IsChannelReadyForInvocations()
        {
            return !_disposing && !_disposed && _state.HasFlag(RpcWorkerChannelState.InvocationBuffersInitialized | RpcWorkerChannelState.Initialized);
        }

        public async Task StartWorkerProcessAsync(CancellationToken cancellationToken)
        {
            RegisterCallbackForNextGrpcMessage(MsgType.StartStream, _workerConfig.CountOptions.ProcessStartupTimeout, 1, SendWorkerInitRequest, HandleWorkerStartStreamError);
            // note: it is important that the ^^^ StartStream is in place *before* we start process the loop, otherwise we get a race condition
            _ = ProcessInbound();

            _workerChannelLogger.LogDebug("Initiating Worker Process start up");
            await _rpcWorkerProcess.StartProcessAsync();
            _state = _state | RpcWorkerChannelState.Initializing;
            await _workerInitTask.Task;
        }

        public async Task<WorkerStatus> GetWorkerStatusAsync()
        {
            var workerStatus = new WorkerStatus();

            if (!string.IsNullOrEmpty(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.WorkerStatus)))
            {
                // get the worker's current status
                // this will include the OOP worker's channel latency in the request, which can be used upstream
                // to make scale decisions
                var message = new StreamingMessage
                {
                    RequestId = Guid.NewGuid().ToString(),
                    WorkerStatusRequest = new WorkerStatusRequest()
                };

                var sw = ValueStopwatch.StartNew();
                var tcs = new TaskCompletionSource<bool>();
                if (_workerStatusRequests.TryAdd(message.RequestId, tcs))
                {
                    await SendStreamingMessageAsync(message);
                    await tcs.Task;
                    var elapsed = sw.GetElapsedTime();
                    workerStatus.Latency = elapsed;
                    _workerChannelLogger.LogDebug("[HostMonitor] Worker status request took {totalMs}ms", elapsed.TotalMilliseconds);
                }
            }

            workerStatus.IsReady = IsChannelReadyForInvocations();
            if (_environment.IsWorkerDynamicConcurrencyEnabled())
            {
                workerStatus.LatencyHistory = GetLatencies();
            }

            return workerStatus;
        }

        // send capabilities to worker, wait for WorkerInitResponse
        internal void SendWorkerInitRequest(GrpcEvent startEvent)
        {
            _workerChannelLogger.LogDebug("Worker Process started. Received StartStream message");
            RegisterCallbackForNextGrpcMessage(MsgType.WorkerInitResponse, _workerConfig.CountOptions.InitializationTimeout, 1, WorkerInitResponse, HandleWorkerInitError);

            WorkerInitRequest initRequest = GetWorkerInitRequest();

            // Run as Functions Host V2 compatible
            if (_environment.IsV2CompatibilityMode())
            {
                _workerChannelLogger.LogDebug("Worker and host running in V2 compatibility mode");
                initRequest.Capabilities.Add(RpcWorkerConstants.V2Compatable, "true");
            }

            if (ScriptHost.IsFunctionDataCacheEnabled)
            {
                // FunctionDataCache is available from the host side - we send this to the worker.
                // As long as the worker replies back with the SharedMemoryDataTransfer capability, the cache
                // can be used.
                initRequest.Capabilities.Add(RpcWorkerConstants.FunctionDataCache, "true");
            }

            // advertise that we support multiple streams, and hint at a number; with this flag, we allow
            // clients to connect multiple back-hauls *with the same workerid*, and rely on the internal
            // plumbing to make sure we don't process everything N times
            initRequest.Capabilities.Add(RpcWorkerConstants.MultiStream, "10"); // TODO: make this configurable

            SendStreamingMessage(new StreamingMessage
            {
                WorkerInitRequest = initRequest
            });
        }

        internal WorkerInitRequest GetWorkerInitRequest()
        {
            return new WorkerInitRequest()
            {
                HostVersion = ScriptHost.Version,
                WorkerDirectory = _workerConfig.Description.WorkerDirectory,
                FunctionAppDirectory = _applicationHostOptions.CurrentValue.ScriptPath
            };
        }

        internal void FunctionEnvironmentReloadResponse(FunctionEnvironmentReloadResponse res, IDisposable latencyEvent)
        {
            _workerChannelLogger.LogDebug("Received FunctionEnvironmentReloadResponse from WorkerProcess with Pid: '{0}'", _rpcWorkerProcess.Id);

            res.WorkerMetadata?.UpdateWorkerMetadata(_workerConfig);

            Utility.ExecuteAfterColdStartDelay(_environment, () => LogWorkerMetadata(res.WorkerMetadata));

            _workerConfig.Description.DefaultRuntimeVersion = _workerConfig.Description.DefaultRuntimeVersion ?? res?.WorkerMetadata?.RuntimeVersion;
            _workerConfig.Description.DefaultRuntimeName = _workerConfig.Description.DefaultRuntimeName ?? res?.WorkerMetadata?.RuntimeName;

            ApplyCapabilities(res.Capabilities, res.CapabilitiesUpdateStrategy.ToGrpcCapabilitiesUpdateStrategy());

            if (res.Result.IsFailure(IsUserCodeExceptionCapabilityEnabled(), out var reloadEnvironmentVariablesException))
            {
                if (res.Result.Exception is not null && reloadEnvironmentVariablesException is not null)
                {
                    _workerChannelLogger.LogWarning(reloadEnvironmentVariablesException, reloadEnvironmentVariablesException.Message);
                }
                _reloadTask.SetResult(false);
            }
            else
            {
                _reloadTask.SetResult(true);
            }
            latencyEvent.Dispose();
        }

        internal void WorkerInitResponse(GrpcEvent initEvent)
        {
            _startLatencyMetric?.Dispose();
            _startLatencyMetric = null;

            _workerChannelLogger.LogDebug("Received WorkerInitResponse. Worker process initialized");
            _initMessage = initEvent.Message.WorkerInitResponse;
            _workerChannelLogger.LogDebug("Worker capabilities: {capabilities}", _initMessage.Capabilities);

            _initMessage.WorkerMetadata?.UpdateWorkerMetadata(_workerConfig);

            Utility.ExecuteAfterColdStartDelay(_environment, () => LogWorkerMetadata(_initMessage.WorkerMetadata));

            _workerConfig.Description.DefaultRuntimeVersion = _workerConfig.Description.DefaultRuntimeVersion ?? _initMessage?.WorkerMetadata?.RuntimeVersion;
            _workerConfig.Description.DefaultRuntimeName = _workerConfig.Description.DefaultRuntimeName ?? _initMessage?.WorkerMetadata?.RuntimeName;

            if (_initMessage.Result.IsFailure(out Exception exc))
            {
                HandleWorkerInitError(exc);
                _workerInitTask.TrySetResult(false);
                return;
            }

            _state = _state | RpcWorkerChannelState.Initialized;

            ApplyCapabilities(_initMessage.Capabilities);

            _workerInitTask.TrySetResult(true);
        }

        private bool IsUserCodeExceptionCapabilityEnabled()
        {
            var enableUserCodeExceptionCapability = string.Equals(
                _workerCapabilities.GetCapabilityState(RpcWorkerConstants.EnableUserCodeException), bool.TrueString,
                StringComparison.OrdinalIgnoreCase);

            return enableUserCodeExceptionCapability;
        }

        private void LogWorkerMetadata(WorkerMetadata workerMetadata)
        {
            if (workerMetadata == null)
            {
                return;
            }

            var workerMetadataString = workerMetadata.ToString();
            _metricsLogger.LogEvent(MetricEventNames.WorkerMetadata, functionName: null, Sanitizer.Sanitize(workerMetadataString));
            _workerChannelLogger.LogDebug("Worker metadata: {workerMetadata}", workerMetadataString);
        }

        // Allow tests to add capabilities, even if not directly supported by the worker.
        internal virtual void UpdateCapabilities(IDictionary<string, string> fields, GrpcCapabilitiesUpdateStrategy strategy)
        {
            _workerCapabilities.UpdateCapabilities(fields, strategy);
        }

        // Helper method that updates and applies capabilities
        // Used at worker initialization and environment reload (placeholder scenarios)
        // The default strategy for updating capabilities is merge
        internal void ApplyCapabilities(IDictionary<string, string> capabilities, GrpcCapabilitiesUpdateStrategy strategy = GrpcCapabilitiesUpdateStrategy.Merge)
        {
            UpdateCapabilities(capabilities, strategy);

            _isSharedMemoryDataTransferEnabled = ResolveSharedTransferEnablementState(_workerCapabilities, _environment, _workerChannelLogger);
            _isHandlesInvocationCancelMessageCapabilityEnabled = !string.IsNullOrEmpty(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.HandlesInvocationCancelMessage));

            if (!_isSharedMemoryDataTransferEnabled)
            {
                // If the worker does not support using shared memory data transfer, caching must also be disabled
                ScriptHost.IsFunctionDataCacheEnabled = false;
            }

            if (_environment.IsApplicationInsightsAgentEnabled() ||
                (bool.TryParse(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.WorkerApplicationInsightsLoggingEnabled), out bool appInsightsWorkerEnabled) &&
                appInsightsWorkerEnabled))
            {
                _isWorkerApplicationInsightsLoggingEnabled = true;
            }

            if (bool.TryParse(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.WorkerOpenTelemetryEnabled), out bool otelEnabled) &&
                otelEnabled)
            {
                ScriptHost.WorkerOpenTelemetryEnabled = true;
            }

            // If http proxying is enabled, we need to get the proxying endpoint of this worker
            var httpUri = _workerCapabilities.GetCapabilityState(RpcWorkerConstants.HttpUri);
            if (!string.IsNullOrEmpty(httpUri))
            {
                try
                {
                    _httpProxyEndpoint = new Uri(httpUri);
                }
                catch (Exception ex)
                {
                    HandleWorkerInitError(ex);
                }
            }
        }

        public void SetupFunctionInvocationBuffers(IEnumerable<FunctionMetadata> functions)
        {
            _functions = functions;
            foreach (FunctionMetadata metadata in functions)
            {
                string functionId = metadata.GetFunctionId();
                _workerChannelLogger.LogDebug("Setting up FunctionInvocationBuffer for function: '{functionName}' with functionId: '{functionId}'", metadata.Name, functionId);
                _functionInputBuffers[functionId] = new BufferBlock<ScriptInvocationContext>();
            }
            _state |= RpcWorkerChannelState.InvocationBuffersInitialized;
        }

        public void SendFunctionLoadRequests(ManagedDependencyOptions managedDependencyOptions, TimeSpan? functionTimeout)
        {
            if (_functions != null)
            {
                // Load Request is also sent for disabled function as it is invocable using the portal and admin endpoints
                // Loading disabled functions at the end avoids unnecessary performance issues. Refer PR #5072 and commit #38b57883be28524fa6ee67a457fa47e96663094c
                _functions = _functions.OrderBy(metadata => metadata.IsDisabled());

                // Check if the worker supports this feature
                bool capabilityEnabled = !string.IsNullOrEmpty(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.SupportsLoadResponseCollection));
                TimeSpan timeout = TimeSpan.Zero;
                if (functionTimeout.HasValue)
                {
                    _functionLoadTimeout = functionTimeout.Value > _functionLoadTimeout ? functionTimeout.Value : _functionLoadTimeout;
                    timeout = _functionLoadTimeout;
                }

                var count = _functions.Count();
                if (capabilityEnabled)
                {
                    RegisterCallbackForNextGrpcMessage(MsgType.FunctionLoadResponseCollection, timeout, count, msg => LoadResponse(msg.Message.FunctionLoadResponseCollection), HandleWorkerFunctionLoadError);

                    SendFunctionLoadRequestCollection(_functions, managedDependencyOptions);
                }
                else
                {
                    RegisterCallbackForNextGrpcMessage(MsgType.FunctionLoadResponse, timeout, count, msg => LoadResponse(msg.Message.FunctionLoadResponse), HandleWorkerFunctionLoadError);

                    foreach (FunctionMetadata metadata in _functions)
                    {
                        SendFunctionLoadRequest(metadata, managedDependencyOptions);
                    }
                }
            }
        }

        internal void SendFunctionLoadRequestCollection(IEnumerable<FunctionMetadata> functions, ManagedDependencyOptions managedDependencyOptions)
        {
            _functionLoadRequestResponseEvent = _metricsLogger.LatencyEvent(MetricEventNames.FunctionLoadRequestResponse);

            FunctionLoadRequestCollection functionLoadRequestCollection = GetFunctionLoadRequestCollection(functions, managedDependencyOptions);

            _workerChannelLogger.LogDebug("Sending FunctionLoadRequestCollection with number of functions: '{count}'", functionLoadRequestCollection.FunctionLoadRequests.Count);

            // send load requests for the registered functions
            SendStreamingMessage(new StreamingMessage
            {
                FunctionLoadRequestCollection = functionLoadRequestCollection
            });
        }

        internal FunctionLoadRequestCollection GetFunctionLoadRequestCollection(IEnumerable<FunctionMetadata> functions, ManagedDependencyOptions managedDependencyOptions)
        {
            var functionLoadRequestCollection = new FunctionLoadRequestCollection();

            foreach (FunctionMetadata metadata in functions)
            {
                var functionLoadRequest = GetFunctionLoadRequest(metadata, managedDependencyOptions);
                functionLoadRequestCollection.FunctionLoadRequests.Add(functionLoadRequest);
            }

            return functionLoadRequestCollection;
        }

        public Task<bool> SendFunctionEnvironmentReloadRequest()
        {
            _functionsIndexingTask = new TaskCompletionSource<List<RawFunctionMetadata>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _functionMetadataRequestSent = false;

            _workerChannelLogger.LogDebug("Sending FunctionEnvironmentReloadRequest to WorkerProcess with Pid: '{0}'", _rpcWorkerProcess.Id);
            IDisposable latencyEvent = _metricsLogger.LatencyEvent(MetricEventNames.SpecializationEnvironmentReloadRequestResponse);

            RegisterCallbackForNextGrpcMessage(MsgType.FunctionEnvironmentReloadResponse, _workerConfig.CountOptions.EnvironmentReloadTimeout, 1,
                msg => FunctionEnvironmentReloadResponse(msg.Message.FunctionEnvironmentReloadResponse, latencyEvent), HandleWorkerEnvReloadError);

            IDictionary processEnv = Environment.GetEnvironmentVariables();

            FunctionEnvironmentReloadRequest request = GetFunctionEnvironmentReloadRequest(processEnv);

            SendStreamingMessage(new StreamingMessage
            {
                FunctionEnvironmentReloadRequest = request
            });

            return _reloadTask.Task;
        }

        public void SendWorkerWarmupRequest()
        {
            bool capabilityEnabled = !string.IsNullOrEmpty(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.HandlesWorkerWarmupMessage));
            if (!capabilityEnabled)
            {
                _workerChannelLogger.LogDebug("Worker warmup capability not enabled");
            }
            else
            {
                _workerChannelLogger.LogDebug("Sending WorkerWarmupRequest to WorkerProcess with Pid: '{0}'", _rpcWorkerProcess.Id);

                RegisterCallbackForNextGrpcMessage(MsgType.WorkerWarmupResponse, TimeSpan.FromMinutes(1.5), 1,
                msg => ProcessWorkerWarmupResponse(msg.Message.WorkerWarmupResponse), HandleWorkerWarmupError);

                var request = new WorkerWarmupRequest()
                {
                    WorkerDirectory = _workerConfig.Description.WorkerDirectory,
                };

                SendStreamingMessage(new StreamingMessage
                {
                    WorkerWarmupRequest = request
                });
            }
        }

        internal void ProcessWorkerWarmupResponse(WorkerWarmupResponse response)
        {
            _workerChannelLogger.LogDebug("Received WorkerWarmupResponse from WorkerProcess with Pid: '{0}'", _rpcWorkerProcess.Id);
            if (response.Result.IsFailure(out Exception workerWarmupException))
            {
                _workerChannelLogger.LogError(workerWarmupException, "Worker warmup failed");
            }
        }

        internal FunctionEnvironmentReloadRequest GetFunctionEnvironmentReloadRequest(IDictionary processEnv)
        {
            foreach (var pair in _hostingConfigOptions.Value.Features)
            {
                processEnv[pair.Key] = pair.Value;
            }

            FunctionEnvironmentReloadRequest request = new FunctionEnvironmentReloadRequest();
            foreach (DictionaryEntry entry in processEnv)
            {
                // Do not add environment variables with empty or null values (see issue #4488 for context)
                if (!string.IsNullOrEmpty(entry.Value?.ToString()))
                {
                    request.EnvironmentVariables.Add(entry.Key.ToString(), entry.Value.ToString());
                }
            }

            string scriptRoot = _applicationHostOptions.CurrentValue.ScriptPath;
            request.EnvironmentVariables.TryAdd(WorkerConstants.FunctionsWorkerDirectorySettingName, _workerConfig.Description.WorkerDirectory);
            request.EnvironmentVariables.TryAdd(WorkerConstants.FunctionsApplicationDirectorySettingName, scriptRoot);
            request.FunctionAppDirectory = scriptRoot;

            return request;
        }

        internal void SendFunctionLoadRequest(FunctionMetadata metadata, ManagedDependencyOptions managedDependencyOptions)
        {
            _functionLoadRequestResponseEvent = _metricsLogger.LatencyEvent(MetricEventNames.FunctionLoadRequestResponse);

            if (_workerChannelLogger.IsEnabled(LogLevel.Debug))
            {
                _workerChannelLogger.LogDebug("Sending FunctionLoadRequest for function: '{functionName}' with functionId: '{functionId}'", metadata.Name, metadata.GetFunctionId());
            }

            // send a load request for the registered function
            SendStreamingMessage(new StreamingMessage
            {
                FunctionLoadRequest = GetFunctionLoadRequest(metadata, managedDependencyOptions)
            });
        }

        internal FunctionLoadRequest GetFunctionLoadRequest(FunctionMetadata metadata, ManagedDependencyOptions managedDependencyOptions)
        {
            FunctionLoadRequest request = new FunctionLoadRequest()
            {
                FunctionId = metadata.GetFunctionId(),
                Metadata = new RpcFunctionMetadata()
                {
                    Name = metadata.Name,
                    Directory = metadata.FunctionDirectory ?? string.Empty,
                    EntryPoint = metadata.EntryPoint ?? string.Empty,
                    ScriptFile = metadata.ScriptFile ?? string.Empty,
                    IsProxy = metadata.IsProxy()
                }
            };

            if (managedDependencyOptions != null && managedDependencyOptions.Enabled)
            {
                _workerChannelLogger?.LogDebug("Adding dependency download request to {language} language worker", _workerConfig.Description.Language);
                request.ManagedDependencyEnabled = managedDependencyOptions.Enabled;
            }

            foreach (var binding in metadata.Bindings)
            {
                BindingInfo bindingInfo = binding.ToBindingInfo();

                request.Metadata.Bindings.Add(binding.Name, bindingInfo);

                if (binding.SupportsDeferredBinding() && !binding.SkipDeferredBinding())
                {
                    _metricsLogger.LogEvent(MetricEventNames.FunctionBindingDeferred, functionName: Sanitizer.Sanitize(metadata.Name));
                }
            }

            foreach (var property in metadata.Properties)
            {
                // worker properties are expected to be string values
                request.Metadata.Properties.Add(property.Key, property.Value?.ToString());
            }

            return request;
        }

        internal void LoadResponse(FunctionLoadResponse loadResponse)
        {
            _functionLoadRequestResponseEvent?.Dispose();
            string functionName = _functions.SingleOrDefault(m => m.GetFunctionId().Equals(loadResponse.FunctionId, StringComparison.OrdinalIgnoreCase))?.Name;
            _workerChannelLogger.LogDebug("Received FunctionLoadResponse for function: '{functionName}' with functionId: '{functionId}'.", functionName, loadResponse.FunctionId);
            if (loadResponse.Result.IsFailure(out Exception functionLoadEx))
            {
                if (functionLoadEx == null)
                {
                    _workerChannelLogger?.LogError("Worker failed to to load function: '{functionName}' with functionId: '{functionId}'. Function load exception is not set by the worker.", functionName, loadResponse.FunctionId);
                }
                else
                {
                    _workerChannelLogger?.LogError(functionLoadEx, "Worker failed to load function: '{functionName}' with functionId: '{functionId}'.", functionName, loadResponse.FunctionId);
                }
                //Cache function load errors to replay error messages on invoking failed functions
                _functionLoadErrors[loadResponse.FunctionId] = functionLoadEx;
            }

            if (loadResponse.IsDependencyDownloaded)
            {
                _workerChannelLogger?.LogDebug("Managed dependency successfully downloaded by the {workerLanguage} language worker", _workerConfig.Description.Language);
            }

            // link the invocation inputs to the invoke call
            var invokeBlock = new ActionBlock<ScriptInvocationContext>(async ctx => await SendInvocationRequest(ctx));
            // associate the invocation input buffer with the function
            var disposableLink = _functionInputBuffers[loadResponse.FunctionId].LinkTo(invokeBlock);
            _inputLinks.Add(disposableLink);
        }

        internal void LoadResponse(FunctionLoadResponseCollection loadResponseCollection)
        {
            _workerChannelLogger.LogDebug("Received FunctionLoadResponseCollection with number of functions: '{count}'.", loadResponseCollection.FunctionLoadResponses.Count);

            foreach (FunctionLoadResponse loadResponse in loadResponseCollection.FunctionLoadResponses)
            {
                LoadResponse(loadResponse);
            }
        }

        internal async Task SendInvocationRequest(ScriptInvocationContext context)
        {
            try
            {
                string invocationId = context.ExecutionContext.InvocationId.ToString();
                string functionId = context.FunctionMetadata.GetFunctionId();

                // do not send an invocation request for functions that failed to load or could not be indexed by the worker
                if (_functionLoadErrors.TryGetValue(functionId, out Exception exception))
                {
                    _workerChannelLogger.LogDebug("Function {functionName} failed to load", context.FunctionMetadata.Name);
                    context.ResultSource.TrySetException(exception);
                    RemoveExecutingInvocation(invocationId);
                    return;
                }
                else if (_metadataRequestErrors.TryGetValue(functionId, out exception))
                {
                    _workerChannelLogger.LogDebug("Worker failed to load metadata for {functionName}", context.FunctionMetadata.Name);
                    context.ResultSource.TrySetException(exception);
                    RemoveExecutingInvocation(invocationId);
                    return;
                }

                if (context.CancellationToken.IsCancellationRequested)
                {
                    _workerChannelLogger.LogDebug("Cancellation was requested prior to the invocation request ('{invocationId}') being sent to the worker.", invocationId);

                    // If the worker does not support handling InvocationCancel grpc messages, or if cancellation is supported and the customer opts-out
                    // of sending cancelled invocations to the worker, we will cancel the result source and not send the invocation to the worker.
                    if (!_isHandlesInvocationCancelMessageCapabilityEnabled || !JobHostOptions.Value.SendCanceledInvocationsToWorker)
                    {
                        _workerChannelLogger.LogInformation("Cancelling invocation '{invocationId}' due to cancellation token being signaled. "
                            + "This invocation was not sent to the worker. Read more about this here: https://aka.ms/azure-functions-cancellations", invocationId);

                        // This will result in an invocation failure with a "FunctionInvocationCanceled" exception.
                        context.ResultSource.TrySetCanceled();
                        return;
                    }
                }

                var invocationRequest = await context.ToRpcInvocationRequest(_workerChannelLogger, _workerCapabilities, _isSharedMemoryDataTransferEnabled, _sharedMemoryManager);
                AddAdditionalTraceContext(invocationRequest.TraceContext.Attributes, context);
                _executingInvocations.TryAdd(invocationRequest.InvocationId, new(context, _messageDispatcherFactory.Create(invocationRequest.InvocationId)));
                _metricsLogger.LogEvent(string.Format(MetricEventNames.WorkerInvoked, Id), functionName: Sanitizer.Sanitize(context.FunctionMetadata.Name));

                // If the worker supports HTTP proxying, ensure this request is forwarded prior
                // to sending the invocation request to the worker, as this will ensure the context
                // is properly set up.
                if (IsHttpProxyingWorker && context.FunctionMetadata.IsHttpTriggerFunction())
                {
                    _httpProxyService.StartForwarding(context, _httpProxyEndpoint);
                }

                await SendStreamingMessageAsync(new StreamingMessage
                {
                    InvocationRequest = invocationRequest
                });

                if (_isHandlesInvocationCancelMessageCapabilityEnabled)
                {
                    var cancellationCtr = context.CancellationToken.Register(() => SendInvocationCancel(invocationRequest.InvocationId));
                    context.Properties.Add(ScriptConstants.CancellationTokenRegistration, cancellationCtr);
                }
            }
            catch (Exception invokeEx)
            {
                context.ResultSource.TrySetException(invokeEx);
            }
        }

        internal void SendInvocationCancel(string invocationId)
        {
            _workerChannelLogger.LogDebug("Sending InvocationCancel request for invocation: '{invocationId}'", invocationId);

            var invocationCancel = new InvocationCancel
            {
                InvocationId = invocationId
            };

            SendStreamingMessage(new StreamingMessage
            {
                InvocationCancel = invocationCancel
            });
        }

        // gets metadata from worker
        public Task<List<RawFunctionMetadata>> GetFunctionMetadata()
        {
            return SendFunctionMetadataRequest();
        }

        internal Task<List<RawFunctionMetadata>> SendFunctionMetadataRequest()
        {
            _workerChannelLogger.LogDebug("Fetching worker metadata, FunctionMetadataReceived set to: {functionMetadataReceived}", _functionMetadataRequestSent);
            if (!_functionMetadataRequestSent)
            {
                lock (_metadataLock)
                {
                    if (!_functionMetadataRequestSent)
                    {
                        RegisterCallbackForNextGrpcMessage(MsgType.FunctionMetadataResponse, _functionLoadTimeout, 1,
                    msg => ProcessFunctionMetadataResponses(msg.Message.FunctionMetadataResponse), HandleWorkerMetadataRequestError);

                        _workerChannelLogger.LogDebug("Sending WorkerMetadataRequest to {language} worker with worker ID {workerID}", _runtime, _workerId);

                        // sends the function app directory path to worker for indexing
                        SendStreamingMessage(new StreamingMessage
                        {
                            FunctionsMetadataRequest = new FunctionsMetadataRequest()
                            {
                                FunctionAppDirectory = _applicationHostOptions.CurrentValue.ScriptPath
                            }
                        });

                        _functionMetadataRequestSent = true;
                    }
                }
            }

            return _functionsIndexingTask.Task;
        }

        // parse metadata response into RawFunctionMetadata objects for AggregateFunctionMetadataProvider to further parse and validate
        internal void ProcessFunctionMetadataResponses(FunctionMetadataResponse functionMetadataResponse)
        {
            _workerChannelLogger.LogDebug("Received the worker function metadata response from worker {worker_id}", _workerId);

            if (functionMetadataResponse.Result.IsFailure(out Exception metadataResponseEx))
            {
                _workerChannelLogger?.LogError(metadataResponseEx, "Worker failed to index functions");
            }

            var functions = new List<RawFunctionMetadata>();

            if (functionMetadataResponse.UseDefaultMetadataIndexing == false)
            {
                foreach (var metadata in functionMetadataResponse.FunctionMetadataResults)
                {
                    if (metadata == null)
                    {
                        continue;
                    }
                    if (metadata.Status != null && metadata.Status.IsFailure(out Exception metadataRequestEx))
                    {
                        _workerChannelLogger.LogError("Worker failed to index function {functionId}", metadata.FunctionId);
                        _metadataRequestErrors[metadata.FunctionId] = metadataRequestEx;
                    }

                    var functionMetadata = new FunctionMetadata()
                    {
                        FunctionDirectory = metadata.Directory,
                        ScriptFile = metadata.ScriptFile,
                        EntryPoint = metadata.EntryPoint,
                        Name = metadata.Name,
                        Language = metadata.Language
                    };

                    if (metadata.RetryOptions is not null)
                    {
                        functionMetadata.Retry = new RetryOptions
                        {
                            MaxRetryCount = metadata.RetryOptions.MaxRetryCount,
                            Strategy = metadata.RetryOptions.RetryStrategy.ToRetryStrategy()
                        };

                        if (functionMetadata.Retry.Strategy is RetryStrategy.FixedDelay)
                        {
                            functionMetadata.Retry.DelayInterval = metadata.RetryOptions.DelayInterval?.ToTimeSpan();
                        }
                        else
                        {
                            functionMetadata.Retry.MinimumInterval = metadata.RetryOptions.MinimumInterval?.ToTimeSpan();
                            functionMetadata.Retry.MaximumInterval = metadata.RetryOptions.MaximumInterval?.ToTimeSpan();
                        }
                    }

                    functionMetadata.SetFunctionId(metadata.FunctionId);

                    foreach (var property in metadata.Properties)
                    {
                        if (!functionMetadata.Properties.TryAdd(property.Key, property.Value?.ToString()))
                        {
                            _workerChannelLogger?.LogDebug("{metadataPropertyKey} is already a part of metadata properties for {functionId}", property.Key, metadata.FunctionId);
                        }
                    }

                    var bindings = new List<string>();
                    foreach (string binding in metadata.RawBindings)
                    {
                        bindings.Add(binding);
                    }

                    functions.Add(new RawFunctionMetadata()
                    {
                        Metadata = functionMetadata,
                        Bindings = bindings,
                        UseDefaultMetadataIndexing = functionMetadataResponse.UseDefaultMetadataIndexing
                    });
                }
            }
            else
            {
                functions.Add(new RawFunctionMetadata()
                {
                    UseDefaultMetadataIndexing = functionMetadataResponse.UseDefaultMetadataIndexing
                });
            }

            // set it as task result because we cannot directly return from SendWorkerMetadataRequest
            _functionsIndexingTask.SetResult(functions);
        }

        private async Task<object> GetBindingDataAsync(ParameterBinding binding, string invocationId)
        {
            switch (binding.RpcDataCase)
            {
                case ParameterBindingType.RpcSharedMemory:
                    // Data was transferred by the worker using shared memory
                    return await binding.RpcSharedMemory.ToObjectAsync(_workerChannelLogger, invocationId, _sharedMemoryManager, ScriptHost.IsFunctionDataCacheEnabled);
                case ParameterBindingType.Data:
                    // Data was transferred by the worker using RPC
                    return binding.Data.ToObject();
                case ParameterBindingType.None:
                    return null;
                default:
                    throw new InvalidOperationException($"Unknown ParameterBindingType of type {binding.RpcDataCase}");
            }
        }

        /// <summary>
        /// From the output data produced by the worker, get a list of the shared memory maps that were created for this invocation.
        /// </summary>
        /// <param name="bindings">List of <see cref="ParameterBinding"/> produced by the worker as output.</param>
        /// <returns>List of names of shared memory maps produced by the worker.</returns>
        private IList<string> GetOutputMaps(IList<ParameterBinding> bindings)
        {
            IList<string> outputMaps = new List<string>();
            foreach (ParameterBinding binding in bindings)
            {
                if (binding.RpcSharedMemory != null)
                {
                    outputMaps.Add(binding.RpcSharedMemory.Name);
                }
            }

            return outputMaps;
        }

        internal async Task InvokeResponse(InvocationResponse invokeResponse)
        {
            Logger.InvocationResponseReceived(_workerChannelLogger, invokeResponse.InvocationId);

            // Check if the worker supports logging user-code-thrown exceptions to app insights
            bool userCodeExceptionHandlingEnabled = !string.IsNullOrEmpty(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.EnableUserCodeException));

            if (_executingInvocations.TryRemove(invokeResponse.InvocationId, out var invocation))
            {
                var context = invocation.Context;

                if (context.Properties.TryGetValue(ScriptConstants.CancellationTokenRegistration, out CancellationTokenRegistration ctr))
                {
                    await ctr.DisposeAsync();
                    context.Properties.Remove(ScriptConstants.CancellationTokenRegistration);
                }

                try
                {
                    if (IsHttpProxyingWorker && context.FunctionMetadata.IsHttpTriggerFunction())
                    {
                        await _httpProxyService.EnsureSuccessfulForwardingAsync(context);
                    }

                    LogSharedMemoryUsage(invokeResponse);

                    if (invokeResponse.Result.IsInvocationSuccess())
                    {
                        ScriptInvocationResult result = await CreateScriptInvocationResult(invokeResponse);
                        context.ResultSource.SetResult(result);

                        _metricsLogger.LogEvent(_workerInvocationSucccededMetric);
                    }
                    else
                    {
                        var rpcException = invokeResponse.Result.GetRpcException(userCodeExceptionHandlingEnabled);
                        context.ResultSource.SetException(rpcException);

                        _metricsLogger.LogEvent(_workerInvocationFailedMetric);
                    }
                }
                catch (Exception exc)
                {
                    context.ResultSource.TrySetException(exc);
                }
                finally
                {
                    TryCloseSharedMemoryResources(invokeResponse);

                    invocation.Dispose();
                }
            }
        }

        private async Task<ScriptInvocationResult> CreateScriptInvocationResult(InvocationResponse invokeResponse)
        {
            IDictionary<string, object> bindingsDictionary = await invokeResponse.OutputData
                .ToDictionaryAsync(binding => binding.Name, binding => GetBindingDataAsync(binding, invokeResponse.InvocationId));

            var result = new ScriptInvocationResult()
            {
                Outputs = bindingsDictionary,
                Return = invokeResponse?.ReturnValue?.ToObject()
            };

            return result;
        }

        private void TryCloseSharedMemoryResources(InvocationResponse invokeResponse)
        {
            try
            {
                if (!_isSharedMemoryDataTransferEnabled)
                {
                    return;
                }

                // Free memory allocated by the host (for input bindings) which was needed only for the duration of this invocation
                if (!_sharedMemoryManager.TryFreeSharedMemoryMapsForInvocation(invokeResponse.InvocationId))
                {
                    _workerChannelLogger.LogWarning("Cannot free all shared memory resources for invocation: {invocationId}", invokeResponse.InvocationId);
                }

                // List of shared memory maps that were produced by the worker (for output bindings)
                IList<string> outputMaps = GetOutputMaps(invokeResponse.OutputData);
                if (outputMaps.Count > 0)
                {
                    // If this invocation was using any shared memory maps produced by the worker, close them to free memory
                    SendCloseSharedMemoryResourcesForInvocationRequest(outputMaps);
                }
            }
            catch (Exception exc)
            {
                _workerChannelLogger.LogWarning(exc, "Failed to cleanup shared memory resources");
            }
        }

        private void LogSharedMemoryUsage(InvocationResponse invokeResponse)
        {
            // Return early if debug logging is not enabled.
            if (!_isSharedMemoryDataTransferEnabled
                || !_workerChannelLogger.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            StringBuilder sharedMemoryLogBuilder = null;

            foreach (ParameterBinding binding in invokeResponse.OutputData)
            {
                switch (binding.RpcDataCase)
                {
                    case ParameterBindingType.RpcSharedMemory:
                        sharedMemoryLogBuilder ??= new StringBuilder();
                        sharedMemoryLogBuilder.AppendFormat("{0}:{1},", binding.Name, binding.RpcSharedMemory.Count);
                        break;
                    default:
                        break;
                }
            }

            if (sharedMemoryLogBuilder != null)
            {
                _workerChannelLogger.LogDebug("Shared memory usage for response of invocation '{invocationId}' is {SharedMemoryUsage}", invokeResponse.InvocationId, sharedMemoryLogBuilder);
            }
        }

        /// <summary>
        /// Request to free memory allocated by the worker (for output bindings).
        /// </summary>
        /// <param name="outputMaps">List of names of shared memory maps to close from the worker.</param>
        internal void SendCloseSharedMemoryResourcesForInvocationRequest(IList<string> outputMaps)
        {
            // Request the worker to drop its references to any shared memory maps that it had produced.
            // This is because the host has read them (or holds a reference to them if caching is enabled.)
            // The worker will not delete the resources allocated for the memory maps; it will only drop its reference
            // so that the worker process does not prevent the OS from freeing the memory maps when the host attempts
            // to free them (either right away after reading them or if caching is enabled, then when the cache decides
            // to evict that object based on its eviction policy).
            CloseSharedMemoryResourcesRequest closeSharedMemoryResourcesRequest = new CloseSharedMemoryResourcesRequest();
            closeSharedMemoryResourcesRequest.MapNames.AddRange(outputMaps);

            SendStreamingMessage(new StreamingMessage()
            {
                CloseSharedMemoryResourcesRequest = closeSharedMemoryResourcesRequest
            });
        }

        internal void Log(GrpcEvent msg)
        {
            var rpcLog = msg.Message.RpcLog;
            if (_executingInvocations.TryGetValue(rpcLog.InvocationId, out var invocation))
            {
                var context = invocation.Context;

                // Restore the execution context from the original invocation. This allows AsyncLocal state to flow to loggers.
                System.Threading.ExecutionContext.Run(context.AsyncExecutionContext, static (state) =>
                {
                    var stateTuple = ((ScriptInvocationContext Context, RpcLog RpcLog, bool AppInsightsEnabledOnWorker))state;

                    var rpcLog = stateTuple.RpcLog;
                    LogLevel logLevel = (LogLevel)rpcLog.Level;

                    var context = stateTuple.Context;

                    if (rpcLog.LogCategory == RpcLogCategory.CustomMetric)
                    {
                        if (rpcLog.PropertiesMap.TryGetValue(LogConstants.NameKey, out var metricName)
                            && rpcLog.PropertiesMap.TryGetValue(LogConstants.MetricValueKey, out var metricValue))
                        {
                            // Strip off the name/value entries in the dictionary passed to Log Message and include the rest as the property bag passed to the backing ILogger
                            var rpcLogProperties = rpcLog.PropertiesMap
                                                    .Where(i => i.Key != LogConstants.NameKey && i.Key != LogConstants.MetricValueKey)
                                                    .ToDictionary(i => i.Key, i => i.Value.ToObject());
                            context.Logger.LogMetric(metricName.String, metricValue.Double, rpcLogProperties);
                        }
                    }
                    else
                    {
                        try
                        {
                            WorkerTraceFilterTelemetryProcessor.FilterApplicationInsightsFromWorker.Value = stateTuple.AppInsightsEnabledOnWorker;

                            if (rpcLog.Exception != null)
                            {
                                // TODO fix RpcException catch all https://github.com/Azure/azure-functions-dotnet-worker/issues/370
                                var exception = new Workers.Rpc.RpcException(rpcLog.Message, rpcLog.Exception.Message, rpcLog.Exception.StackTrace);
                                context.Logger.Log(logLevel, new EventId(0, rpcLog.EventId), rpcLog.Message, exception, (state, exc) => state);
                            }
                            else
                            {
                                context.Logger.Log(logLevel, new EventId(0, rpcLog.EventId), rpcLog.Message, null, (state, exc) => state);
                            }
                        }
                        finally
                        {
                            WorkerTraceFilterTelemetryProcessor.FilterApplicationInsightsFromWorker.Value = false;
                        }
                    }
                }, (context, rpcLog, _isWorkerApplicationInsightsLoggingEnabled));
            }
        }

        internal void SystemLog(GrpcEvent msg)
        {
            RpcLog systemLog = msg.Message.RpcLog;
            LogLevel logLevel = (LogLevel)systemLog.Level;
            switch (logLevel)
            {
                case LogLevel.Warning:
                    _workerChannelLogger.LogWarning(systemLog.Message);
                    break;

                case LogLevel.Information:
                    _workerChannelLogger.LogInformation(systemLog.Message);
                    break;

                case LogLevel.Error:
                    {
                        if (systemLog.Exception != null)
                        {
                            Workers.Rpc.RpcException exception = new Workers.Rpc.RpcException(systemLog.Message, systemLog.Exception.Message, systemLog.Exception.StackTrace);
                            _workerChannelLogger.LogError(exception, systemLog.Message);
                        }
                        else
                        {
                            _workerChannelLogger.LogError(systemLog.Message);
                        }
                    }
                    break;

                default:
                    _workerChannelLogger.LogInformation(systemLog.Message);
                    break;
            }
        }

        internal void HandleWorkerStartStreamError(Exception exc)
        {
            _workerChannelLogger.LogError(exc, "Starting worker process failed");
            PublishWorkerErrorEvent(exc);
        }

        internal void HandleWorkerEnvReloadError(Exception exc)
        {
            _workerChannelLogger.LogError(exc, "Reloading environment variables failed");
            _reloadTask.SetException(exc);
        }

        internal void HandleWorkerInitError(Exception exc)
        {
            _workerChannelLogger.LogError(exc, "Initializing worker process failed");
            PublishWorkerErrorEvent(exc);
        }

        internal void HandleWorkerFunctionLoadError(Exception exc)
        {
            _workerChannelLogger.LogError(exc, "Loading function failed.");
            if (_disposing || _disposed)
            {
                return;
            }
            _eventManager.Publish(new WorkerErrorEvent(_runtime, Id, exc));
        }

        private void PublishWorkerErrorEvent(Exception exc)
        {
            _workerInitTask.TrySetException(exc);
            if (_disposing || _disposed)
            {
                return;
            }
            _eventManager.Publish(new WorkerErrorEvent(_runtime, Id, exc));
        }

        internal void HandleWorkerMetadataRequestError(Exception exc)
        {
            _workerChannelLogger.LogError(exc, "Requesting metadata from worker failed.");
            if (_disposing || _disposed)
            {
                return;
            }
            _eventManager.Publish(new WorkerErrorEvent(_runtime, Id, exc));
        }

        private void HandleWorkerWarmupError(Exception exc)
        {
            _workerChannelLogger.LogError(exc, "Worker warmup failed");
        }

        private ValueTask SendStreamingMessageAsync(StreamingMessage msg)
        {
            var evt = new OutboundGrpcEvent(_workerId, msg);
            return _outbound.TryWrite(evt) ? default : _outbound.WriteAsync(evt);
        }

        private void SendStreamingMessage(StreamingMessage msg)
        {
            var evt = new OutboundGrpcEvent(_workerId, msg);
            if (!_outbound.TryWrite(evt))
            {
                var pending = _outbound.WriteAsync(evt);
                if (pending.IsCompleted)
                {
                    try
                    {
                        pending.GetAwaiter().GetResult(); // ensure observed to ensure the IValueTaskSource completed/result is consumed
                    }
                    catch
                    {
                        // suppress failure
                    }
                }
                else
                {
                    _ = ObserveEventually(pending);
                }
            }
            static async Task ObserveEventually(ValueTask valueTask)
            {
                try
                {
                    await valueTask;
                }
                catch
                {
                    // no where to log
                }
            }
        }

        internal void ReceiveWorkerStatusResponse(string requestId, WorkerStatusResponse response)
        {
            if (_workerStatusRequests.TryRemove(requestId, out var workerStatusTask))
            {
                workerStatusTask.SetResult(true);
            }
        }

        private void RemoveExecutingInvocation(string invocationId)
        {
            if (_executingInvocations.TryRemove(invocationId, out var invocation))
            {
                invocation.Dispose();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    foreach (var id in _executingInvocations.Keys)
                    {
                        RemoveExecutingInvocation(id);
                    }

                    _startLatencyMetric?.Dispose();
                    _workerInitTask?.TrySetCanceled();
                    _timer?.Dispose();
                    _scriptHostManager.ActiveHostChanged -= HandleActiveHostChange;

                    // unlink function inputs
                    if (_inputLinks is not null)
                    {
                        foreach (var link in _inputLinks)
                        {
                            if (link is null)
                            {
                                // This log is temporarily added for diagnostic purposes.
                                _workerChannelLogger.LogDebug("An input link is null. Skipping disposal.");
                            }

                            link?.Dispose();
                        }
                    }
                    else
                    {
                        // This log is temporarily added for diagnostic purposes.
                        _workerChannelLogger.LogDebug("The input links collection is null. Skipping disposal of any individual input links.");
                    }

                    (_rpcWorkerProcess as IDisposable)?.Dispose();

                    if (_eventSubscriptions is not null)
                    {
                        foreach (var sub in _eventSubscriptions)
                        {
                            if (sub is null)
                            {
                                // This log is temporarily added for diagnostic purposes.
                                _workerChannelLogger.LogDebug("An event subscription is null. Skipping disposal.");
                            }

                            sub?.Dispose();
                        }
                    }
                    else
                    {
                        // This log is temporarily added for diagnostic purposes.
                        _workerChannelLogger.LogDebug("The event subscriptions collection is null. Skipping disposal of any individual subscriptions.");
                    }

                    // shut down the channels
                    _eventManager.RemoveGrpcChannels(_workerId);
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            StopWorkerProcess();
            _disposing = true;
            Dispose(true);
        }

        private void StopWorkerProcess()
        {
            bool capabilityEnabled = !string.IsNullOrEmpty(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.HandlesWorkerTerminateMessage));
            if (!capabilityEnabled)
            {
                return;
            }

            int gracePeriod = WorkerConstants.WorkerTerminateGracePeriodInSeconds;

            var workerTerminate = new WorkerTerminate()
            {
                GracePeriod = Duration.FromTimeSpan(TimeSpan.FromSeconds(gracePeriod))
            };

            _workerChannelLogger.LogDebug("Sending WorkerTerminate message with grace period of {gracePeriod} seconds.", gracePeriod);

            SendStreamingMessage(new StreamingMessage
            {
                WorkerTerminate = workerTerminate
            });

            WorkerProcess.WaitForProcessExitInMilliSeconds(gracePeriod * 1000);
        }

        public async Task DrainInvocationsAsync()
        {
            _workerChannelLogger.LogDebug("Count of in-buffer invocations waiting to be drained out: {invocationCount}", _executingInvocations.Count);
            foreach (var invocation in _executingInvocations.Values)
            {
                await invocation.Context.ResultSource.Task;
            }
        }

        public bool IsExecutingInvocation(string invocationId)
        {
            return _executingInvocations.ContainsKey(invocationId);
        }

        public bool TryFailExecutions(Exception workerException)
        {
            if (workerException == null)
            {
                return false;
            }

            foreach (var invocation in _executingInvocations?.Values)
            {
                string invocationId = invocation.Context?.ExecutionContext?.InvocationId.ToString();
                _workerChannelLogger.LogDebug("Worker '{workerId}' encountered a fatal error. Failing invocation: '{invocationId}'", _workerId, invocationId);
                invocation.Context?.ResultSource?.TrySetException(workerException);
                RemoveExecutingInvocation(invocationId);
            }
            return true;
        }

        /// <summary>
        /// Determine if shared memory transfer is enabled.
        /// The following conditions must be met:
        ///     1) <see cref="RpcWorkerConstants.FunctionsWorkerSharedMemoryDataTransferEnabledSettingName"/> must be set in environment variable (AppSetting).
        ///     2) Worker must have the capability <see cref="RpcWorkerConstants.SharedMemoryDataTransfer"/>.
        /// </summary>
        /// <returns><see cref="true"/> if shared memory data transfer is enabled, <see cref="false"/> otherwise.</returns>
        internal static bool ResolveSharedTransferEnablementState(GrpcCapabilities workerCapabilities, IEnvironment environment, ILogger logger)
        {
            // Check if the environment variable (AppSetting) has this feature enabled
            string envVal = environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerSharedMemoryDataTransferEnabledSettingName);
            if (string.IsNullOrEmpty(envVal))
            {
                return false;
            }

            bool envValEnabled = false;
            if (bool.TryParse(envVal, out bool boolResult))
            {
                // Check if value was specified as a bool (true/false)
                envValEnabled = boolResult;
            }
            else if (int.TryParse(envVal, out int intResult) && intResult == 1)
            {
                // Check if value was specified as an int (1/0)
                envValEnabled = true;
            }

            if (!envValEnabled)
            {
                return false;
            }

            // Check if the worker supports this feature
            bool capabilityEnabled = !string.IsNullOrEmpty(workerCapabilities.GetCapabilityState(RpcWorkerConstants.SharedMemoryDataTransfer));
            logger.LogDebug("IsSharedMemoryDataTransferEnabled: {SharedMemoryDataTransferEnabled}", capabilityEnabled);

            return capabilityEnabled;
        }

        internal void EnsureTimerStarted()
        {
            if (_environment.IsWorkerDynamicConcurrencyEnabled())
            {
                lock (_syncLock)
                {
                    if (_timer == null)
                    {
                        _timer = new System.Timers.Timer()
                        {
                            AutoReset = false,
                            Interval = _workerConcurrencyOptions.Value.CheckInterval.TotalMilliseconds,
                        };

                        _timer.Elapsed += OnTimer;
                        _timer.Start();
                    }
                }
            }
        }

        internal IEnumerable<TimeSpan> GetLatencies()
        {
            EnsureTimerStarted();
            lock (_syncLock)
            {
                return _workerStatusLatencyHistory.ToArray();
            }
        }

        internal async void OnTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                WorkerStatus workerStatus = await GetWorkerStatusAsync();
                AddSample(_workerStatusLatencyHistory, workerStatus.Latency);
            }
            catch
            {
                // Don't allow background exceptions to escape
                // E.g. when a rpc channel is shutting down we can process exceptions
            }
            try
            {
                _timer.Start();
            }
            catch (ObjectDisposedException)
            {
                // Specifically ignore this race - we're exiting and that's okay
            }
        }

        private void AddSample<T>(List<T> samples, T sample)
        {
            lock (_syncLock)
            {
                if (samples.Count == _workerConcurrencyOptions.Value.HistorySize)
                {
                    samples.RemoveAt(0);
                }
                samples.Add(sample);
            }
        }

        private void AddAdditionalTraceContext(MapField<string, string> attributes, ScriptInvocationContext context)
        {
            bool isOtelEnabled = _scriptHostOptions?.Value.TelemetryMode == TelemetryMode.OpenTelemetry;
            bool isAIEnabled = _environment.IsApplicationInsightsAgentEnabled();

            if (isOtelEnabled || isAIEnabled)
            {
                if (context.FunctionMetadata.Properties.TryGetValue(ScriptConstants.LogPropertyHostInstanceIdKey, out var hostInstanceIdValue))
                {
                    string id = Convert.ToString(hostInstanceIdValue);

                    if (isOtelEnabled)
                    {
                        Activity.Current?.AddTag(ResourceSemanticConventions.FaaSInstance, id);
                    }
                    if (isAIEnabled)
                    {
                        attributes[ScriptConstants.LogPropertyHostInstanceIdKey] = id;
                    }
                }
            }

            if (isAIEnabled)
            {
                attributes[ScriptConstants.LogPropertyProcessIdKey] = Convert.ToString(_rpcWorkerProcess.Id);
                attributes[ScriptConstants.OperationNameKey] = context.FunctionMetadata.Name;
                string sessionId = Activity.Current?.GetBaggageItem(ScriptConstants.LiveLogsSessionAIKey);
                if (!string.IsNullOrEmpty(sessionId))
                {
                    attributes[ScriptConstants.LiveLogsSessionAIKey] = sessionId;
                }

                if (context.FunctionMetadata.Properties.TryGetValue(LogConstants.CategoryNameKey, out var categoryNameValue))
                {
                    attributes[LogConstants.CategoryNameKey] = Convert.ToString(categoryNameValue);
                }
            }

            if (isOtelEnabled)
            {
                Activity.Current?.AddTag(ResourceSemanticConventions.FaaSName, context.FunctionMetadata.Name);
            }
        }

        private sealed class ExecutingInvocation : IDisposable
        {
            public ExecutingInvocation(ScriptInvocationContext context, IInvocationMessageDispatcher dispatcher)
            {
                Context = context;
                Dispatcher = dispatcher;
            }

            public ScriptInvocationContext Context { get; }

            public IInvocationMessageDispatcher Dispatcher { get; }

            public void Dispose()
            {
                (Dispatcher as IDisposable)?.Dispose();
            }
        }

        private sealed class PendingItem
        {
            private readonly Action<InboundGrpcEvent> _callback;
            private readonly Action<Exception> _faultHandler;
            private CancellationTokenRegistration _ctr;
            private int _state;

            public PendingItem(Action<InboundGrpcEvent> callback, Action<Exception> faultHandler)
            {
                _callback = callback;
                _faultHandler = faultHandler;
            }

            public PendingItem(Action<InboundGrpcEvent> callback, Action<Exception> faultHandler, TimeSpan timeout)
                : this(callback, faultHandler)
            {
                var cts = new CancellationTokenSource();
                cts.CancelAfter(timeout);
                _ctr = cts.Token.Register(static state => ((PendingItem)state).OnTimeout(), this);
            }

            public bool IsComplete => Volatile.Read(ref _state) != 0;

            private bool MakeComplete() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

            public void SetResult(InboundGrpcEvent message)
            {
                _ctr.Dispose();
                _ctr = default;
                if (MakeComplete() && _callback != null)
                {
                    try
                    {
                        _callback.Invoke(message);
                    }
                    catch (Exception fault)
                    {
                        try
                        {
                            _faultHandler?.Invoke(fault);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            private void OnTimeout()
            {
                if (MakeComplete() && _faultHandler != null)
                {
                    try
                    {
                        throw new TimeoutException();
                    }
                    catch (Exception timeout)
                    {
                        try
                        {
                            _faultHandler(timeout);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }
    }
}