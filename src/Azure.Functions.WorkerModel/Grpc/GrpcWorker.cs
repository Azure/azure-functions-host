using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Azure.Functions.WorkerModel.Configuration;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Extensions;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using OutOfProcModel.Abstractions.Mock;
using OutOfProcModel.Abstractions.Worker;
using static Microsoft.Azure.WebJobs.Script.Grpc.Messages.RpcLog.Types;

namespace OutOfProcModel.FunctionsHost.Grpc;

internal class GrpcWorker : IWorker, IAsyncDisposable
{
    private readonly BidirectionalChannel _channel;
    private readonly FunctionApplicationOptions _appOptions;
    private readonly Task _readLoopTask;
    private ConcurrentDictionary<string, ExecutingInvocation> _executingInvocations = new();
    private readonly TaskCompletionSource<ImmutableArray<FunctionMetadata>> _functionMetadataTcs = new();
    private readonly TaskCompletionSource<ImmutableArray<FunctionMetadata>> _functionLoadTcs = new();
    private readonly CancellationTokenSource _readLoopCancellationSource = new();
    private readonly ILogger _workerChannelLogger = NullLogger.Instance;
    private readonly GrpcCapabilities _workerCapabilities;
    private readonly IInvocationMessageDispatcherFactory _messageDispatcherFactory;
    private readonly WaitCallback _processInbound;
    private readonly ISharedMemoryManager _sharedMemoryManager;

    private ImmutableDictionary<string, TaskCompletionSource> _loadedFunctions;

    private Uri? _httpProxyEndpoint;
    private IHttpProxyService _httpProxyService;
    private ImmutableArray<FunctionMetadata> _functions;

    public GrpcWorker(WorkerDefinition workerDefinition, IOptions<FunctionApplicationOptions> appOptions, ISharedMemoryManager sharedMemoryManager, IHttpProxyService httpProxyService)
    {
        Definition = workerDefinition ?? throw new ArgumentNullException(nameof(workerDefinition));
        _channel = new BidirectionalChannel();
        _appOptions = appOptions.Value;
        _sharedMemoryManager = sharedMemoryManager;

        // TODO: should be in some kind of StartAsync()?
        _readLoopTask = StartReadLoopAsync(_readLoopCancellationSource.Token);

        _workerCapabilities = new GrpcCapabilities(_workerChannelLogger);

        _messageDispatcherFactory = new OrderedInvocationMessageDispatcherFactory(ProcessItem, _workerChannelLogger);
        _httpProxyService = httpProxyService;

        _processInbound = state => ProcessItem((StreamingMessage)state);

        ApplyCapabilities(workerDefinition.Capabilities);
    }

    public WorkerDefinition Definition { get; }

    public WorkerStatus Status { get; private set; } = WorkerStatus.Created;

    public IExternalWorkerChannel Channel => _channel;

    private bool IsHttpProxyingWorker => _httpProxyEndpoint is not null;

    private async Task StartReadLoopAsync(CancellationToken readLoopToken)
    {
        Status = WorkerStatus.Running;
        try
        {
            while (await _channel.HostMessageReader.WaitToReadAsync(readLoopToken))
            {
                while (_channel.HostMessageReader.TryRead(out MessageFromWorker? message))
                {
                    DispatchMessage(message.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private void DispatchMessage(StreamingMessage msg)
    {
        // RpcLog and InvocationResponse messages are special. They need to be handled by the InvocationMessageDispatcher
        switch (msg.ContentCase)
        {
            case StreamingMessage.ContentOneofCase.RpcLog when msg.RpcLog.LogCategory == RpcLogCategory.User || msg.RpcLog.LogCategory == RpcLogCategory.CustomMetric:
                if (_executingInvocations.TryGetValue(msg.RpcLog.InvocationId, out var invocation))
                {
                    invocation.Dispatcher.DispatchRpcLog(msg);
                }
                else
                {
                    // We received a log outside of a invocation
                    ThreadPool.QueueUserWorkItem(_processInbound, msg);
                }
                break;
            case StreamingMessage.ContentOneofCase.InvocationResponse:
                if (_executingInvocations.TryGetValue(msg.InvocationResponse.InvocationId, out invocation))
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


    private void ProcessItem(StreamingMessage msg)
    {
        // note this method is a thread-pool (QueueUserWorkItem) entry-point
        try
        {
            switch (msg.ContentCase)
            {
                case StreamingMessage.ContentOneofCase.RpcLog when msg.RpcLog.LogCategory == RpcLogCategory.System:
                    // SystemLog(msg);
                    break;
                case StreamingMessage.ContentOneofCase.RpcLog:
                    // Log(msg);
                    break;
                case StreamingMessage.ContentOneofCase.WorkerStatusResponse:
                    // ReceiveWorkerStatusResponse(msg.Message.RequestId, msg.Message.WorkerStatusResponse);
                    break;
                case StreamingMessage.ContentOneofCase.InvocationResponse:
                    _ = InvokeResponse(msg.InvocationResponse);
                    break;
                case StreamingMessage.ContentOneofCase.FunctionMetadataResponse:
                    _ = HandleFunctionMetadataResponse(msg.FunctionMetadataResponse);
                    break;
                case StreamingMessage.ContentOneofCase.FunctionLoadResponse:
                    HandleFunctionLoadResponse(msg.FunctionLoadResponse);
                    break;
                default:
                    // ProcessRegisteredGrpcCallbacks(msg);
                    break;
            }
        }
        catch (Exception ex)
        {
            _workerChannelLogger.LogError(ex, "Error processing InboundGrpcEvent: " + ex.Message);
        }
    }

    internal async Task InvokeResponse(InvocationResponse invokeResponse)
    {
        // Logger.InvocationResponseReceived(_workerChannelLogger, invokeResponse.InvocationId);

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

                // LogSharedMemoryUsage(invokeResponse);

                if (invokeResponse.Result.IsInvocationSuccess())
                {
                    ScriptInvocationResult result = await CreateScriptInvocationResult(invokeResponse);
                    context.ResultSource.SetResult(result);

                    // _metricsLogger.LogEvent(_workerInvocationSucccededMetric);
                }
                else
                {
                    var rpcException = invokeResponse.Result.GetRpcException(userCodeExceptionHandlingEnabled);
                    context.SetException(rpcException);

                    // _metricsLogger.LogEvent(_workerInvocationFailedMetric);
                }
            }
            catch (Exception exc)
            {
                context.SetException(exc);
            }
            finally
            {
                // TryCloseSharedMemoryResources(invokeResponse);

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

    private async Task<object> GetBindingDataAsync(ParameterBinding binding, string invocationId)
    {
        switch (binding.RpcDataCase)
        {
            case ParameterBinding.RpcDataOneofCase.RpcSharedMemory:
                // Data was transferred by the worker using shared memory
                return await binding.RpcSharedMemory.ToObjectAsync(_workerChannelLogger, invocationId, _sharedMemoryManager, ScriptHost.IsFunctionDataCacheEnabled);
            case ParameterBinding.RpcDataOneofCase.Data:
                // Data was transferred by the worker using RPC
                return binding.Data.ToObject();
            case ParameterBinding.RpcDataOneofCase.None:
                return null;
            default:
                throw new InvalidOperationException($"Unknown ParameterBindingType of type {binding.RpcDataCase}");
        }
    }

    public Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync()
    {
        if (!_functions.IsDefault)
        {
            return Task.FromResult(_functions);
        }

        _channel.WorkerMessageWriter.TryWrite(new MessageToWorker(new StreamingMessage
        {
            FunctionsMetadataRequest = new FunctionsMetadataRequest
            {
                FunctionAppDirectory = _appOptions.ApplicationRoot
            }
        }));

        return _functionMetadataTcs.Task;
    }

    private async Task HandleFunctionMetadataResponse(FunctionMetadataResponse functionMetadataResponse)
    {
        var functions = new List<RawFunctionMetadata>();

        if (functionMetadataResponse.UseDefaultMetadataIndexing == false)
        {
            foreach (var metadata in functionMetadataResponse.FunctionMetadataResults)
            {
                if (metadata == null)
                {
                    continue;
                }

                //if (metadata.Status != null && metadata.Status.IsFailure(out Exception metadataRequestEx))
                //{
                //    _workerChannelLogger.LogError("Worker failed to index function {functionId}", metadata.FunctionId);
                //    _metadataRequestErrors[metadata.FunctionId] = metadataRequestEx;
                //}

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
                        //_workerChannelLogger?.LogDebug("{metadataPropertyKey} is already a part of metadata properties for {functionId}", property.Key, metadata.FunctionId);
                    }
                }

                var bindings = new List<string>();
                foreach (string binding in metadata.RawBindings)
                {
                    bindings.Add(binding);
                    functionMetadata.Bindings.Add(BindingMetadata.Create(JObject.Parse(binding)));
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

        // TODO: Match what ValidateBindings does in the original code

        // Use to prevent invoking functions until we know it's loaded (replaces FunctionInvocationBuffers). Needs re-thinking? Use another set of channels?
        _loadedFunctions = functions.ToDictionary(f => f.Metadata.GetFunctionId(), _ => new TaskCompletionSource()).ToImmutableDictionary();

        var allMetadata = functions.Select(f => f.Metadata);
        bool supportsLoadResponseCollection = !string.IsNullOrEmpty(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.SupportsLoadResponseCollection));

        SendFunctionLoadRequests(allMetadata, supportsLoadResponseCollection);

        _functions = allMetadata.ToImmutableArray();
        _functionMetadataTcs.TrySetResult(_functions);
    }

    public async Task<ScriptInvocationResult> InvokeAsync(ScriptInvocationContext context)
    {
        if (Status != WorkerStatus.Running)
        {
            throw new InvalidOperationException($"Worker {Definition.WorkerId} is not running. Current status: {Status}");
        }

        var functionId = context.FunctionMetadata.GetFunctionId();
        if (!_loadedFunctions[functionId].Task.IsCompletedSuccessfully)
        {
            await _loadedFunctions[functionId].Task;
        }

        await SendInvocationRequest(context);

        return await context.ResultSource.Task;
    }

    internal async Task SendInvocationRequest(ScriptInvocationContext context)
    {
        try
        {
            string invocationId = context.ExecutionContext.InvocationId.ToString();
            string functionId = context.FunctionMetadata.GetFunctionId();

            // do not send an invocation request for functions that failed to load or could not be indexed by the worker
            //if (_functionLoadErrors.TryGetValue(functionId, out Exception exception))
            //{
            //    _workerChannelLogger.LogDebug("Function {functionName} failed to load", context.FunctionMetadata.Name);
            //    context.SetException(exception);
            //    RemoveExecutingInvocation(invocationId);
            //    return;
            //}
            //else if (_metadataRequestErrors.TryGetValue(functionId, out exception))
            //{
            //    _workerChannelLogger.LogDebug("Worker failed to load metadata for {functionName}", context.FunctionMetadata.Name);
            //    context.SetException(exception);
            //    RemoveExecutingInvocation(invocationId);
            //    return;
            //}

            //if (context.CancellationToken.IsCancellationRequested)
            //{
            //    _workerChannelLogger.LogDebug("Cancellation was requested prior to the invocation request ('{invocationId}') being sent to the worker.", invocationId);

            //    // If the worker does not support handling InvocationCancel grpc messages, or if cancellation is supported and the customer opts-out
            //    // of sending cancelled invocations to the worker, we will cancel the result source and not send the invocation to the worker.
            //    if (!_isHandlesInvocationCancelMessageCapabilityEnabled || !JobHostOptions.Value.SendCanceledInvocationsToWorker)
            //    {
            //        _workerChannelLogger.LogInformation("Cancelling invocation '{invocationId}' due to cancellation token being signaled. "
            //            + "This invocation was not sent to the worker. Read more about this here: https://aka.ms/azure-functions-cancellations", invocationId);

            //        // This will result in an invocation failure with a "FunctionInvocationCanceled" exception.
            //        context.ResultSource.TrySetCanceled();
            //        return;
            //    }
            //}

            var invocationRequest = await context.ToRpcInvocationRequest(_workerChannelLogger, _workerCapabilities, false, null);
            // AddAdditionalTraceContext(invocationRequest, context);
            _executingInvocations.TryAdd(invocationRequest.InvocationId, new(context, _messageDispatcherFactory.Create(invocationRequest.InvocationId)));
            // _metricsLogger.LogEvent(string.Format(MetricEventNames.WorkerInvoked, Id), functionName: Sanitizer.Sanitize(context.FunctionMetadata.Name));

            // If the worker supports HTTP proxying, ensure this request is forwarded prior
            // to sending the invocation request to the worker, as this will ensure the context
            // is properly set up.
            if (IsHttpProxyingWorker && context.FunctionMetadata.IsHttpTriggerFunction())
            {
                _httpProxyService.StartForwarding(context, _httpProxyEndpoint);
            }

            _channel.WorkerMessageWriter.TryWrite(new MessageToWorker(new StreamingMessage
            {
                InvocationRequest = invocationRequest
            }));

            //if (_isHandlesInvocationCancelMessageCapabilityEnabled)
            //{
            //    var cancellationCtr = context.CancellationToken.Register(() => SendInvocationCancel(invocationRequest.InvocationId));
            //    context.Properties.Add(ScriptConstants.CancellationTokenRegistration, cancellationCtr);
            //}
        }
        catch (Exception invokeEx)
        {
            context.SetException(invokeEx);
        }
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

        // _isSharedMemoryDataTransferEnabled = ResolveSharedTransferEnablementState(_workerCapabilities, _environment, _workerChannelLogger);
        // _isHandlesInvocationCancelMessageCapabilityEnabled = !string.IsNullOrEmpty(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.HandlesInvocationCancelMessage));

        //if (!_isSharedMemoryDataTransferEnabled)
        //{
        //    // If the worker does not support using shared memory data transfer, caching must also be disabled
        //    ScriptHost.IsFunctionDataCacheEnabled = false;
        //}

        //if (_environment.IsApplicationInsightsAgentEnabled() ||
        //    (bool.TryParse(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.WorkerApplicationInsightsLoggingEnabled), out bool appInsightsWorkerEnabled) &&
        //    appInsightsWorkerEnabled))
        //{
        //    _isWorkerApplicationInsightsLoggingEnabled = true;
        //}

        //if (bool.TryParse(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.WorkerOpenTelemetryEnabled), out bool otelEnabled) &&
        //    otelEnabled)
        //{
        //    ScriptHost.WorkerOpenTelemetryEnabled = true;
        //}

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
                // HandleWorkerInitError(ex);
            }
        }
    }

    private void SendFunctionLoadRequests(IEnumerable<FunctionMetadata> functions, bool supportsLoadResponseCollection)
    {
        // Load Request is also sent for disabled function as it is invocable using the portal and admin endpoints
        // Loading disabled functions at the end avoids unnecessary performance issues. Refer PR #5072 and commit #38b57883be28524fa6ee67a457fa47e96663094c
        functions = functions.OrderBy(metadata => metadata.IsDisabled());

        // Check if the worker supports this feature
        // bool capabilityEnabled = !string.IsNullOrEmpty(_workerCapabilities.GetCapabilityState(RpcWorkerConstants.SupportsLoadResponseCollection));
        //TimeSpan timeout = TimeSpan.Zero;
        //if (functionTimeout.HasValue)
        //{
        //    _functionLoadTimeout = functionTimeout.Value > _functionLoadTimeout ? functionTimeout.Value : _functionLoadTimeout;
        //    timeout = _functionLoadTimeout;
        //}

        if (supportsLoadResponseCollection)
        {
            var count = functions.Count();
            //RegisterCallbackForNextGrpcMessage(MsgType.FunctionLoadResponseCollection, timeout, count, msg => LoadResponse(msg.Message.FunctionLoadResponseCollection), HandleWorkerFunctionLoadError);

            //SendFunctionLoadRequestCollection(_functions, managedDependencyOptions);
        }
        else
        {
            foreach (FunctionMetadata metadata in functions)
            {
                // TODO: Review managed dependency stuff?
                SendFunctionLoadRequest(metadata);
            }
        }
    }

    internal void SendFunctionLoadRequest(FunctionMetadata metadata)
    {
        // _functionLoadRequestResponseEvent = _metricsLogger.LatencyEvent(MetricEventNames.FunctionLoadRequestResponse);

        if (_workerChannelLogger.IsEnabled(LogLevel.Debug))
        {
            _workerChannelLogger.LogDebug("Sending FunctionLoadRequest for function: '{functionName}' with functionId: '{functionId}'", metadata.Name, metadata.GetFunctionId());
        }

        // send a load request for the registered function
        _channel.WorkerMessageWriter.TryWrite(new MessageToWorker(new StreamingMessage
        {
            FunctionLoadRequest = GetFunctionLoadRequest(metadata, null)
        }));
    }

    internal FunctionLoadRequest GetFunctionLoadRequest(FunctionMetadata metadata, ManagedDependencyOptions? managedDependencyOptions)
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
            // _workerChannelLogger?.LogDebug("Adding dependency download request to {language} language worker", _workerConfig.Description.Language);
            request.ManagedDependencyEnabled = managedDependencyOptions.Enabled;
        }

        foreach (var binding in metadata.Bindings)
        {
            BindingInfo bindingInfo = binding.ToBindingInfo();

            request.Metadata.Bindings.Add(binding.Name, bindingInfo);

            if (binding.SupportsDeferredBinding() && !binding.SkipDeferredBinding())
            {
                // _metricsLogger.LogEvent(MetricEventNames.FunctionBindingDeferred, functionName: Sanitizer.Sanitize(metadata.Name));
            }
        }

        foreach (var property in metadata.Properties)
        {
            // worker properties are expected to be string values
            request.Metadata.Properties.Add(property.Key, property.Value?.ToString());
        }

        return request;
    }

    internal void HandleFunctionLoadResponse(FunctionLoadResponse loadResponse)
    {
        // _functionLoadRequestResponseEvent?.Dispose();
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
            // _functionLoadErrors[loadResponse.FunctionId] = functionLoadEx;
        }

        if (loadResponse.IsDependencyDownloaded)
        {
            // _workerChannelLogger?.LogDebug("Managed dependency successfully downloaded by the {workerLanguage} language worker", _workerConfig.Description.Language);
        }

        // link the invocation inputs to the invoke call
        // var invokeBlock = new ActionBlock<ScriptInvocationContext>(async ctx => await SendInvocationRequest(ctx));
        // associate the invocation input buffer with the function
        // var disposableLink = _functionInputBuffers[loadResponse.FunctionId].LinkTo(invokeBlock);
        //_inputLinks.Add(disposableLink);

        _loadedFunctions[loadResponse.FunctionId].TrySetResult();
    }

    // Waits for invocations to complete
    public async Task DrainAsync(TimeSpan timeout)
    {
        // This worker is now removed from load-balancing and will not receive new invocations
        Status = WorkerStatus.Draining;

        var tasks = _executingInvocations.Select(p => p.Value.Context.ResultSource.Task);
        await Task.WhenAll(tasks); // todo -- some timeout stuff

        _readLoopCancellationSource.Cancel();
        await _readLoopTask;

        Status = WorkerStatus.Drained;
    }

    // Do not wait for invocations to complete
    private async Task StopAsync()
    {
        Status = WorkerStatus.Stopping;

        _readLoopCancellationSource.Cancel();
        await _readLoopTask;

        Status = WorkerStatus.Stopped;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        if (_channel is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
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
}