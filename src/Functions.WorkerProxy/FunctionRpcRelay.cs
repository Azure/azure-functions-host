// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Channels;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Identifies which side of the relay a gRPC stream belongs to.
/// </summary>
internal enum RelaySide
{
    Worker,
    Runtime
}

/// <summary>
/// Configuration for the worker proxy relay.
/// </summary>
/// <param name="RuntimeGrpcPort">Port the Functions runtime connects to.</param>
/// <param name="WorkerGrpcPort">Port the language worker connects to.</param>
/// <param name="HttpProxyPort">Port the HTTP proxy listens on (rewritten into HttpUri capability).</param>
/// <param name="HostJsonPath">Optional path to a host.json to inject into WorkerInitResponse capabilities.</param>
/// <param name="HttpProxyEndpoint">
/// Full URL advertised in the <c>HttpUri</c> capability so the runtime routes HTTP
/// requests through this proxy. Defaults to <c>http://localhost:{HttpProxyPort}</c>;
/// override when running in containers where <c>localhost</c> is not reachable across
/// container boundaries.
/// </param>
internal record RelayOptions(int RuntimeGrpcPort, int WorkerGrpcPort, int HttpProxyPort, string? HostJsonPath, string HttpProxyEndpoint);

/// <summary>
/// Manages the gRPC relay between the Functions runtime and a language worker.
/// <para>
/// Unlike a simple passthrough, the proxy drives the worker initialization lifecycle
/// during <c>/admin/worker/assign</c>: it sends <c>WorkerInitRequest</c>,
/// <c>FunctionEnvironmentReloadRequest</c>, and <c>FunctionsMetadataRequest</c> to the
/// worker, caching all responses. When the runtime later connects via <c>/admin/runtime/linkWorker</c>,
/// the proxy replays cached responses for <c>WorkerInitRequest</c> and <c>FunctionsMetadataRequest</c>
/// instead of forwarding to the worker. All post-init messages (FunctionLoadRequest,
/// InvocationRequest, etc.) are relayed as a direct passthrough.
/// </para>
/// </summary>
internal sealed class FunctionRpcRelay : FunctionRpc.FunctionRpcBase
{
    private readonly RelayOptions _options;
    private readonly ILogger<FunctionRpcRelay> _logger;
    private readonly WorkerPodStateManager _stateManager;

    // Channels that buffer messages flowing in each direction.
    internal readonly Channel<StreamingMessage> _toWorker = Channel.CreateUnbounded<StreamingMessage>();
    private readonly Channel<StreamingMessage> _toRuntime = Channel.CreateUnbounded<StreamingMessage>();

    // Signals raised once each side has called EventStream.
    private readonly TaskCompletionSource _runtimeConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource _workerConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Cached state from worker initialization (populated by SpecializeWorkerAsync).
    private StreamingMessage? _cachedStartStream;
    internal StreamingMessage? _cachedWorkerInitResponse;
    internal StreamingMessage? _cachedFunctionMetadataResponse;

    // Gate that blocks the runtime's WorkerInitRequest until /assign completes.
    // On success, TrySetResult is called. On failure, TrySetException propagates
    // the error so the runtime-side await throws instead of silently proceeding.
    private readonly TaskCompletionSource _specializationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // TCS for correlating request/response pairs during /assign.
    internal TaskCompletionSource<StreamingMessage>? _pendingWorkerResponse;

    // Guard against concurrent or repeated /assign calls.
    private int _specializationStarted;

    public FunctionRpcRelay(RelayOptions options, ILogger<FunctionRpcRelay> logger, WorkerPodStateManager stateManager)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    }

    /// <summary>
    /// Drives the full worker initialization and specialization sequence.
    /// Called by the <c>POST /admin/worker/assign</c> handler.
    /// <para>
    /// Sends <c>WorkerInitRequest</c>, <c>FunctionEnvironmentReloadRequest</c>, and
    /// <c>FunctionsMetadataRequest</c> to the worker in order, caching all responses.
    /// The runtime later receives these cached responses without re-querying the worker.
    /// </para>
    /// </summary>
    public async Task SpecializeWorkerAsync(
        Dictionary<string, string> environmentVariables,
        string functionAppDirectory,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _specializationStarted, 1, 0) != 0)
        {
            throw new InvalidOperationException("Worker specialization has already been initiated.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(2));
        var token = cts.Token;

        // Wait for the worker to have sent StartStream.
        await _workerConnected.Task.WaitAsync(token);
        _logger.LogInformation("Worker is connected. Starting specialization sequence.");

        try
        {
            // 1. WorkerInitRequest → WorkerInitResponse
            _logger.LogInformation("Sending WorkerInitRequest to worker.");
            var initResponse = await SendAndWaitAsync(
                new StreamingMessage { WorkerInitRequest = new WorkerInitRequest() },
                StreamingMessage.ContentOneofCase.WorkerInitResponse,
                token);

            _logger.LogInformation("Received WorkerInitResponse.");

            var initStatus = initResponse.WorkerInitResponse?.Result?.Status;
            if (initStatus == StatusResult.Types.Status.Failure)
            {
                var errorMsg = initResponse.WorkerInitResponse?.Result?.Exception?.Message
                    ?? "Worker initialization failed.";
                throw new InvalidOperationException(errorMsg);
            }

            // 2. FunctionEnvironmentReloadRequest → FunctionEnvironmentReloadResponse
            var reloadRequest = new FunctionEnvironmentReloadRequest
            {
                FunctionAppDirectory = functionAppDirectory
            };

            foreach (var kvp in environmentVariables)
            {
                reloadRequest.EnvironmentVariables[kvp.Key] = kvp.Value;
            }

            // The worker SDK expects this env var to be set during FunctionLoad.
            reloadRequest.EnvironmentVariables.TryAdd("FUNCTIONS_APPLICATION_DIRECTORY", functionAppDirectory);

            _logger.LogInformation("Sending FunctionEnvironmentReloadRequest (functionAppDirectory={dir}).", functionAppDirectory);
            var reloadResponse = await SendAndWaitAsync(
                new StreamingMessage { FunctionEnvironmentReloadRequest = reloadRequest },
                StreamingMessage.ContentOneofCase.FunctionEnvironmentReloadResponse,
                token);

            var status = reloadResponse.FunctionEnvironmentReloadResponse?.Result?.Status;
            if (status == StatusResult.Types.Status.Failure)
            {
                var errorMsg = reloadResponse.FunctionEnvironmentReloadResponse?.Result?.Exception?.Message
                    ?? "Worker specialization failed.";
                throw new InvalidOperationException(errorMsg);
            }

            _logger.LogInformation("Worker specialization succeeded.");

            // The worker reports its full capabilities in the env reload response
            // (after specialization), not in the initial WorkerInitResponse. Apply
            // them into the cached init response so the runtime receives them.
            // Honor the worker's update strategy (merge or replace), matching the
            // runtime's GrpcCapabilities.UpdateCapabilities behavior.
            var reloadCapabilities = reloadResponse.FunctionEnvironmentReloadResponse?.Capabilities;
            if (reloadCapabilities is not null)
            {
                var envReloadResponse = reloadResponse.FunctionEnvironmentReloadResponse!;
                var initCapabilities = initResponse.WorkerInitResponse.Capabilities;
                var strategy = envReloadResponse.CapabilitiesUpdateStrategy;

                if (strategy == FunctionEnvironmentReloadResponse.Types.CapabilitiesUpdateStrategy.Replace)
                {
                    initCapabilities.Clear();
                }

                foreach (var cap in reloadCapabilities)
                {
                    initCapabilities[cap.Key] = cap.Value;
                }
            }

            // Now that we know the function app directory, inject host.json and rewrite
            // the HttpUri capability so the runtime routes HTTP requests through this proxy.
            InjectHostJson(initResponse, functionAppDirectory);
            RewriteHttpUri(initResponse);

            var capabilities = initResponse.WorkerInitResponse?.Capabilities;
            if (capabilities is not null)
            {
                foreach (var cap in capabilities)
                {
                    _logger.LogInformation("WorkerInitResponse capability: {Key} = {Value}", cap.Key, cap.Value);
                }
            }

            // 3. FunctionsMetadataRequest → FunctionMetadataResponse (prefetch)
            _logger.LogInformation("Prefetching function metadata.");
            var metadataResponse = await SendAndWaitAsync(
                new StreamingMessage { FunctionsMetadataRequest = new FunctionsMetadataRequest() },
                StreamingMessage.ContentOneofCase.FunctionMetadataResponse,
                token);

            _cachedFunctionMetadataResponse = metadataResponse;
            _logger.LogInformation("Cached FunctionMetadataResponse.");

            // Only cache the fully-mutated init response after all enrichment steps
            // have succeeded. This ensures the runtime never sees a stale/incomplete response.
            _cachedWorkerInitResponse = initResponse;
            _logger.LogInformation("Cached WorkerInitResponse.");

            // Signal success — unblock any runtime waiting on WorkerInitRequest.
            _specializationCompleted.TrySetResult();
        }
        catch (Exception ex)
        {
            // Signal failure — the runtime-side await will throw, preventing it
            // from proceeding with a stale or missing cached response.
            _specializationCompleted.TrySetException(ex);
            throw;
        }
    }

    /// <summary>
    /// Sends a <c>WorkerDrainRequest</c> to the runtime over the gRPC stream.
    /// Called when NNA calls <c>POST /drain</c> on the worker proxy.
    /// </summary>
    public async Task SendDrainRequestToRuntimeAsync()
    {
        var message = new StreamingMessage
        {
            WorkerDrainRequest = new WorkerDrainRequest()
        };

        await _toRuntime.Writer.WriteAsync(message);
        _logger.LogInformation("Sent WorkerDrainRequest to runtime.");
    }

    /// <inheritdoc />
    public override async Task EventStream(
        IAsyncStreamReader<StreamingMessage> requestStream,
        IServerStreamWriter<StreamingMessage> responseStream,
        ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        int localPort = httpContext.Connection.LocalPort;

        if (localPort == _options.RuntimeGrpcPort)
        {
            _logger.LogInformation("Runtime connected on port {Port}", localPort);
            _runtimeConnected.TrySetResult();

            // Wait for the worker to be connected before starting the relay.
            await _workerConnected.Task.WaitAsync(context.CancellationToken);

            // Replay cached StartStream to the runtime.
            if (_cachedStartStream is not null)
            {
                await responseStream.WriteAsync(_cachedStartStream);
                _logger.LogInformation("Replayed cached StartStream to runtime.");
            }

            // Runtime reads from _toRuntime, writes into _toWorker.
            await RelayAsync(requestStream, responseStream, _toWorker, _toRuntime, RelaySide.Runtime, context.CancellationToken);
        }
        else
        {
            _logger.LogInformation("Worker connected on port {Port}", localPort);

            // Start the relay immediately. The proxy needs to read worker messages
            // (for /assign) and write to the worker (WorkerInitRequest, etc.) before
            // the runtime connects.
            _stateManager.UpdateHealthStatus(WorkerPodHealthStatus.Healthy);

            await RelayAsync(requestStream, responseStream, _toRuntime, _toWorker, RelaySide.Worker, context.CancellationToken);
        }
    }

    /// <summary>
    /// Sends a message to the worker and waits for a response with the expected content type.
    /// </summary>
    private async Task<StreamingMessage> SendAndWaitAsync(
        StreamingMessage request,
        StreamingMessage.ContentOneofCase expectedResponseType,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<StreamingMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingWorkerResponse = tcs;

        try
        {
            await _toWorker.Writer.WriteAsync(request, cancellationToken);

            var response = await tcs.Task.WaitAsync(cancellationToken);

            if (response.ContentCase != expectedResponseType)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedResponseType} but received {response.ContentCase}.");
            }

            return response;
        }
        finally
        {
            // Clear the pending reference so a late-arriving worker response doesn't
            // get swallowed by ReadInboundAsync after a timeout or cancellation.
            Interlocked.CompareExchange(ref _pendingWorkerResponse, null, tcs);
        }
    }

    /// <summary>
    /// Injects <c>host_configuration_json</c> into the cached <c>WorkerInitResponse</c>.
    /// Resolution order:
    /// 1. Try <c>{functionAppDirectory}/host.json</c> (shared content mount).
    /// 2. Try the explicit <see cref="RelayOptions.HostJsonPath"/> override.
    /// </summary>
    internal void InjectHostJson(StreamingMessage message, string functionAppDirectory)
    {
        if (message.ContentCase != StreamingMessage.ContentOneofCase.WorkerInitResponse)
        {
            return;
        }

        var capabilities = message.WorkerInitResponse.Capabilities;

        // 1. Try reading from the function app directory.
        if (!string.IsNullOrEmpty(functionAppDirectory))
        {
            string appDirHostJson = Path.Combine(functionAppDirectory, "host.json");
            if (File.Exists(appDirHostJson))
            {
                capabilities["host_configuration_json"] = File.ReadAllText(appDirHostJson);
                _logger.LogInformation("Injected host.json from function app directory '{path}'.", appDirHostJson);
                return;
            }
        }

        // 2. Explicit HostJsonPath option.
        if (!string.IsNullOrEmpty(_options.HostJsonPath) && File.Exists(_options.HostJsonPath))
        {
            capabilities["host_configuration_json"] = File.ReadAllText(_options.HostJsonPath);
            _logger.LogInformation("Injected host.json from explicit path '{path}'.", _options.HostJsonPath);
            return;
        }

        _logger.LogWarning("No host.json found. The runtime will use a default configuration.");
    }

    private void RewriteHttpUri(StreamingMessage message)
    {
        if (message.ContentCase != StreamingMessage.ContentOneofCase.WorkerInitResponse)
        {
            return;
        }

        // Always set HttpUri to the proxy's endpoint. The worker may or may not have
        // reported its own HttpUri — in container mode it often doesn't because
        // its HTTP listener address isn't known at init time.
        message.WorkerInitResponse.Capabilities["HttpUri"] = _options.HttpProxyEndpoint;
        _logger.LogInformation("Set HttpUri capability to {Uri}.", _options.HttpProxyEndpoint);
    }

    private async Task RelayAsync(
        IAsyncStreamReader<StreamingMessage> inbound,
        IServerStreamWriter<StreamingMessage> outbound,
        Channel<StreamingMessage> sendChannel,
        Channel<StreamingMessage> receiveChannel,
        RelaySide side,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var readTask = ReadInboundAsync(inbound, sendChannel.Writer, receiveChannel.Writer, side, cts.Token);
        var writeTask = WriteOutboundAsync(receiveChannel.Reader, outbound, cts.Token);

        try
        {
            await Task.WhenAny(readTask, writeTask);
        }
        finally
        {
            await cts.CancelAsync();
            try { await readTask; } catch { }
            try { await writeTask; } catch { }
            _logger.LogInformation("[{Side}] stream disconnected", side);
        }
    }

    private async Task ReadInboundAsync(
        IAsyncStreamReader<StreamingMessage> inbound,
        ChannelWriter<StreamingMessage> forwardWriter,
        ChannelWriter<StreamingMessage> replyWriter,
        RelaySide side,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await inbound.MoveNext(cancellationToken))
            {
                var message = inbound.Current;
                _logger.LogDebug("[{Side}] → {Content}", side, message.ContentCase);

                if (message.ContentCase == StreamingMessage.ContentOneofCase.InvocationRequest)
                {
                    _logger.LogDebug("[{Side}] → InvocationRequest for function '{FunctionId}', invocation '{InvocationId}'",
                        side, message.InvocationRequest.FunctionId, message.InvocationRequest.InvocationId);
                }
                else if (message.ContentCase == StreamingMessage.ContentOneofCase.InvocationResponse)
                {
                    _logger.LogDebug("[{Side}] → InvocationResponse for invocation '{InvocationId}', result: {Result}",
                        side, message.InvocationResponse.InvocationId, message.InvocationResponse.Result?.Status);
                }

                if (side == RelaySide.Worker)
                {
                    // --- Worker-side interception ---

                    // Cache StartStream and signal worker connected. Do NOT relay.
                    if (message.ContentCase == StreamingMessage.ContentOneofCase.StartStream)
                    {
                        _cachedStartStream = message;
                        _workerConnected.TrySetResult();
                        _stateManager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
                        _logger.LogInformation("Cached StartStream from worker '{workerId}'.", message.StartStream?.WorkerId);
                        continue;
                    }

                    // CS-TODO: RpcLog messages from the worker during /assign are buffered in the
                    // _toRuntime channel and flushed to the runtime when it connects. However, they
                    // arrive as an unordered burst before the runtime processes WorkerInitResponse.
                    // Consider caching RpcLog messages and replaying them in order after the runtime
                    // receives the cached init response, so worker startup logs appear correctly in
                    // the runtime's log stream.

                    // During /assign, complete the pending request/response correlation.
                    // These messages are consumed by SpecializeWorkerAsync, not relayed.
                    // Capture to a local to avoid a race where SendAndWaitAsync's finally
                    // block nulls the field between our null-check and TrySetResult.
                    var pending = _pendingWorkerResponse;
                    if (pending is not null
                        && message.ContentCase is StreamingMessage.ContentOneofCase.WorkerInitResponse
                            or StreamingMessage.ContentOneofCase.FunctionEnvironmentReloadResponse
                            or StreamingMessage.ContentOneofCase.FunctionMetadataResponse)
                    {
                        _logger.LogDebug("Completing pending worker response: {Content}", message.ContentCase);
                        pending.TrySetResult(message);
                        _pendingWorkerResponse = null;
                        continue;
                    }
                }
                else
                {
                    // --- Runtime-side interception ---

                    // Intercept WorkerInitRequest — respond from cache. Block until /assign completes.
                    if (message.ContentCase == StreamingMessage.ContentOneofCase.WorkerInitRequest)
                    {
                        _logger.LogInformation("Runtime sent WorkerInitRequest. Waiting for specialization to complete.");

                        try
                        {
                            await _specializationCompleted.Task.WaitAsync(cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Worker specialization failed. Cannot serve WorkerInitResponse to runtime.");
                            continue;
                        }

                        if (_cachedWorkerInitResponse is not null)
                        {
                            _logger.LogInformation("Responding to runtime WorkerInitRequest from cache.");
                            await replyWriter.WriteAsync(_cachedWorkerInitResponse, cancellationToken);
                        }

                        continue;
                    }

                    // Intercept FunctionsMetadataRequest — respond from cache.
                    if (message.ContentCase == StreamingMessage.ContentOneofCase.FunctionsMetadataRequest
                        && _cachedFunctionMetadataResponse is not null)
                    {
                        _logger.LogInformation("Responding to runtime FunctionsMetadataRequest from cache.");
                        await replyWriter.WriteAsync(_cachedFunctionMetadataResponse, cancellationToken);
                        continue;
                    }

                    // Intercept WorkerDrainRequest from runtime — update state machine.
                    if (message.ContentCase == StreamingMessage.ContentOneofCase.WorkerDrainRequest)
                    {
                        _logger.LogInformation("Received WorkerDrainRequest from runtime.");
                        _stateManager.UpdatePodStatus(WorkerPodStatus.Draining);
                        continue;
                    }

                    // Intercept WorkerDrainComplete from runtime — update state machine.
                    if (message.ContentCase == StreamingMessage.ContentOneofCase.WorkerDrainComplete)
                    {
                        _logger.LogInformation("Received WorkerDrainComplete from runtime.");
                        _stateManager.UpdatePodStatus(WorkerPodStatus.DrainCompleted);
                        _stateManager.UpdatePodStatus(WorkerPodStatus.MarkForDeletion);
                        continue;
                    }
                }

                // Default: relay the message to the other side.
                await forwardWriter.WriteAsync(message, cancellationToken);
            }

            forwardWriter.TryComplete();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            forwardWriter.TryComplete(ex);
            throw;
        }
        catch (OperationCanceledException)
        {
            forwardWriter.TryComplete();
        }
    }

    private static async Task WriteOutboundAsync(
        ChannelReader<StreamingMessage> reader,
        IServerStreamWriter<StreamingMessage> outbound,
        CancellationToken cancellationToken)
    {
        await foreach (var message in reader.ReadAllAsync(cancellationToken))
        {
            await outbound.WriteAsync(message);
        }
    }
}
