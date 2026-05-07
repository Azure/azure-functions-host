// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using GrpcWorkerDrainRequest = Microsoft.Azure.WebJobs.Script.Grpc.Messages.WorkerDrainRequest;

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
/// <param name="PodName">Identity of this worker pod for <c>instanceState</c> publication.</param>
internal record RelayOptions(int RuntimeGrpcPort, int WorkerGrpcPort, int HttpProxyPort, string? HostJsonPath, string HttpProxyEndpoint, string PodName);

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
    private const string FunctionsNetHostRpcLogPrefix = "FunctionsNetHost:";
    private const string NoCorrelationValue = "<none>";
    private const string UnknownFunctionValue = "<unknown>";

    private enum HostJsonSource
    {
        FunctionAppDirectory,
        ExplicitPath
    }

    private sealed record HostJsonReadResult(string Path, string Content, HostJsonSource Source);
    private sealed record InvocationCorrelationContext(string FunctionId, string FunctionName, string TraceParent);

    private readonly RelayOptions _options;
    private readonly ILogger<FunctionRpcRelay> _logger;
    private readonly WorkerPodStateManager _stateManager;
    private readonly ConcurrentDictionary<string, string> _functionNamesById = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, InvocationCorrelationContext> _pendingInvocations = new(StringComparer.Ordinal);

    // Channels that buffer messages flowing in each direction.
    internal readonly Channel<StreamingMessage> _toWorker = Channel.CreateUnbounded<StreamingMessage>();
    private readonly Channel<StreamingMessage> _toRuntime = Channel.CreateUnbounded<StreamingMessage>();

    // Signals raised once each side has called EventStream.
    private readonly TaskCompletionSource _runtimeConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource _workerConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Cached state from worker initialization (populated by SpecializeWorkerAsync).
    internal StreamingMessage? _cachedStartStream;
    internal StreamingMessage? _cachedWorkerInitResponse;
    internal StreamingMessage? _cachedFunctionMetadataResponse;

    // Gate that blocks the runtime's WorkerInitRequest until /assign completes.
    // On success, TrySetResult is called. On failure, TrySetException propagates
    // the error so the runtime-side await throws instead of silently proceeding.
    private readonly TaskCompletionSource _specializationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // TCS for correlating request/response pairs during /assign.
    internal TaskCompletionSource<StreamingMessage>? _pendingWorkerResponse;

    // HTTP endpoint advertised by the worker in its WorkerInitResponse "HttpUri" capability.
    // Populated once per worker lifetime (during /assign); read by the HTTP forwarding
    // middleware to route invocations to the worker's dynamically-chosen port.
    internal volatile string? _workerHttpEndpoint;

    /// <summary>
    /// HTTP endpoint advertised by the worker via the <c>HttpUri</c> capability in
    /// <c>WorkerInitResponse</c>. Null until the worker has completed init.
    /// </summary>
    public string? WorkerHttpEndpoint => _workerHttpEndpoint;

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
        var specializeStart = Stopwatch.GetTimestamp();

        if (Interlocked.CompareExchange(ref _specializationStarted, 1, 0) != 0)
        {
            throw new InvalidOperationException("Worker specialization has already been initiated.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(2));
        var token = cts.Token;

        // Wait for the worker to have sent StartStream.
        var workerConnectedWaitStart = Stopwatch.GetTimestamp();
        await _workerConnected.Task.WaitAsync(token);
        _logger.LogInformation("WorkerProxy specialization started. WorkerConnectedWaitElapsedMilliseconds: {workerConnectedWaitElapsedMilliseconds}, ElapsedMilliseconds: {elapsedMilliseconds}.", Stopwatch.GetElapsedTime(workerConnectedWaitStart).TotalMilliseconds, Stopwatch.GetElapsedTime(specializeStart).TotalMilliseconds);

        try
        {
            // 1. WorkerInitRequest → WorkerInitResponse
            var initStart = Stopwatch.GetTimestamp();
            _logger.LogInformation("WorkerProxy sending WorkerInitRequest to worker.");
            var initResponse = await SendAndWaitAsync(
                new StreamingMessage { WorkerInitRequest = new WorkerInitRequest() },
                StreamingMessage.ContentOneofCase.WorkerInitResponse,
                token);

            _logger.LogInformation("WorkerProxy received WorkerInitResponse. StepElapsedMilliseconds: {stepElapsedMilliseconds}, ElapsedMilliseconds: {elapsedMilliseconds}.", Stopwatch.GetElapsedTime(initStart).TotalMilliseconds, Stopwatch.GetElapsedTime(specializeStart).TotalMilliseconds);

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

            var hostJsonReadTask = ReadHostJsonAsync(functionAppDirectory, token);

            _logger.LogInformation("WorkerProxy sending FunctionEnvironmentReloadRequest. FunctionAppDirectory: {dir}.", functionAppDirectory);
            var reloadStart = Stopwatch.GetTimestamp();
            var reloadResponse = await SendAndWaitAsync(
                new StreamingMessage { FunctionEnvironmentReloadRequest = reloadRequest },
                StreamingMessage.ContentOneofCase.FunctionEnvironmentReloadResponse,
                token);

            var status = reloadResponse.FunctionEnvironmentReloadResponse?.Result?.Status;
            var capabilitiesCount = reloadResponse.FunctionEnvironmentReloadResponse?.Capabilities.Count ?? 0;
            _logger.LogInformation("Received FunctionEnvironmentReloadResponse. Status: {Status}, CapabilitiesCount: {CapabilitiesCount}, ElapsedMilliseconds: {ElapsedMilliseconds}.",
                status,
                capabilitiesCount,
                Stopwatch.GetElapsedTime(reloadStart).TotalMilliseconds);

            if (status == StatusResult.Types.Status.Failure)
            {
                var errorMsg = reloadResponse.FunctionEnvironmentReloadResponse?.Result?.Exception?.Message
                    ?? "Worker specialization failed.";
                throw new InvalidOperationException(errorMsg);
            }

            _logger.LogInformation("WorkerProxy worker specialization succeeded. ElapsedMilliseconds: {elapsedMilliseconds}.", Stopwatch.GetElapsedTime(specializeStart).TotalMilliseconds);

            // The worker reports its full capabilities in the env reload response
            // (after specialization), not in the initial WorkerInitResponse. Apply
            // them into the cached init response so the runtime receives them.
            // Honor the worker's update strategy (merge or replace), matching the
            // runtime's GrpcCapabilities.UpdateCapabilities behavior.
            var reloadCapabilities = reloadResponse.FunctionEnvironmentReloadResponse?.Capabilities;
            if (reloadCapabilities is not null)
            {
                var envReloadResponse = reloadResponse.FunctionEnvironmentReloadResponse!;
                var strategy = envReloadResponse.CapabilitiesUpdateStrategy;
                var initCapabilities = initResponse.WorkerInitResponse!.Capabilities;

                _logger.LogInformation("Worker capabilities received by proxy ({Strategy}): {Capabilities}",
                    strategy,
                    CapabilityLogFormatter.Format(reloadCapabilities));

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
            InjectHostJson(initResponse, await hostJsonReadTask);

            // RewriteHttpUri MUST run AFTER the capability merge above. The merge produces
            // the final, post-specialization capability set; rewriting before it would
            // capture a stale or missing HttpUri (e.g. an init-only value that a Replace-
            // strategy reload was supposed to drop). The
            // SpecializeWorkerAsync_WorkerHttpEndpoint_* tests pin this ordering — if you
            // reorder these calls and those tests still pass, the tests have rotted.
            RewriteHttpUri(initResponse);

            _logger.LogInformation("WorkerProxy capabilities to runtime: {Capabilities}",
                CapabilityLogFormatter.Format(initResponse.WorkerInitResponse!.Capabilities));

            // Only cache the fully-mutated init response after all enrichment steps
            // have succeeded. This ensures the runtime never sees a stale/incomplete response.
            _cachedWorkerInitResponse = initResponse;
            _logger.LogInformation("WorkerProxy cached WorkerInitResponse. ElapsedMilliseconds: {elapsedMilliseconds}.", Stopwatch.GetElapsedTime(specializeStart).TotalMilliseconds);

            // Signal success — unblock any runtime waiting on WorkerInitRequest.
            _specializationCompleted.TrySetResult();
            _logger.LogInformation("WorkerProxy specialization completed. TotalElapsedMilliseconds: {totalElapsedMilliseconds}.", Stopwatch.GetElapsedTime(specializeStart).TotalMilliseconds);

            // 3. FunctionsMetadataRequest → FunctionMetadataResponse (opportunistic prefetch)
            // This warms the cache for the runtime, but no longer blocks the
            // specialization gate. If the runtime requests metadata before the cache
            // is populated, the request is relayed to the worker normally.
            _ = PrefetchFunctionMetadataAsync(specializeStart, cancellationToken);
        }
        catch (Exception ex)
        {
            // Signal failure — the runtime-side await will throw, preventing it
            // from proceeding with a stale or missing cached response.
            _specializationCompleted.TrySetException(ex);
            _logger.LogError(ex, "WorkerProxy specialization failed. TotalElapsedMilliseconds: {totalElapsedMilliseconds}.", Stopwatch.GetElapsedTime(specializeStart).TotalMilliseconds);
            throw;
        }
    }

    private async Task PrefetchFunctionMetadataAsync(
        long specializeStart,
        CancellationToken cancellationToken)
    {
        var metadataStart = Stopwatch.GetTimestamp();
        _logger.LogInformation("WorkerProxy prefetching function metadata.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(2));

        try
        {
            var metadataResponse = await SendAndWaitAsync(
                new StreamingMessage { FunctionsMetadataRequest = new FunctionsMetadataRequest() },
                StreamingMessage.ContentOneofCase.FunctionMetadataResponse,
                cts.Token);

            TrackFunctionMetadataResponse(metadataResponse.FunctionMetadataResponse);
            _cachedFunctionMetadataResponse = metadataResponse;
            _logger.LogInformation("WorkerProxy cached FunctionMetadataResponse. StepElapsedMilliseconds: {stepElapsedMilliseconds}, ElapsedMilliseconds: {elapsedMilliseconds}.", Stopwatch.GetElapsedTime(metadataStart).TotalMilliseconds, Stopwatch.GetElapsedTime(specializeStart).TotalMilliseconds);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "WorkerProxy function metadata prefetch was canceled. StepElapsedMilliseconds: {stepElapsedMilliseconds}, ElapsedMilliseconds: {elapsedMilliseconds}.", Stopwatch.GetElapsedTime(metadataStart).TotalMilliseconds, Stopwatch.GetElapsedTime(specializeStart).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "WorkerProxy function metadata prefetch failed. StepElapsedMilliseconds: {stepElapsedMilliseconds}, ElapsedMilliseconds: {elapsedMilliseconds}.", Stopwatch.GetElapsedTime(metadataStart).TotalMilliseconds, Stopwatch.GetElapsedTime(specializeStart).TotalMilliseconds);
        }
    }

    /// <summary>
    /// Sends a <c>WorkerDrainRequest</c> to the runtime over the gRPC stream.
    /// Called when NNA calls <c>POST /admin/worker/drain</c> on the worker proxy.
    /// </summary>
    public async Task SendDrainRequestToRuntimeAsync()
    {
        var message = new StreamingMessage
        {
            WorkerDrainRequest = new GrpcWorkerDrainRequest()
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
            var runtimeConnectTimestamp = Stopwatch.GetTimestamp();
            var workerAlreadyConnected = _workerConnected.Task.IsCompleted;
            _logger.LogInformation("WorkerProxy runtime stream connected. Port: {Port}, WorkerAlreadyConnected: {WorkerAlreadyConnected}.", localPort, workerAlreadyConnected);
            _runtimeConnected.TrySetResult();

            // Start relay immediately without blocking on _workerConnected. Each
            // runtime stream gets StartStream replayed from cache once the worker has
            // connected; WorkerInitRequest remains gated by _specializationCompleted
            // in ReadInboundAsync, preserving correctness.
            await RelayAsync(requestStream, responseStream, _toWorker, _toRuntime, RelaySide.Runtime, context.CancellationToken);
            _logger.LogInformation("WorkerProxy runtime relay completed. Port: {Port}, TotalElapsedMilliseconds: {TotalElapsedMilliseconds}.", localPort, Stopwatch.GetElapsedTime(runtimeConnectTimestamp).TotalMilliseconds);
        }
        else
        {
            _logger.LogInformation("WorkerProxy worker stream connected. Port: {Port}.", localPort);

            // Start the relay immediately. The proxy needs to read worker messages
            // (for /assign) and write to the worker (WorkerInitRequest, etc.) before
            // the runtime connects.
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
        => InjectHostJson(message, ReadHostJson(functionAppDirectory));

    private void InjectHostJson(StreamingMessage message, HostJsonReadResult? hostJson)
    {
        if (message.ContentCase != StreamingMessage.ContentOneofCase.WorkerInitResponse)
        {
            return;
        }

        var capabilities = message.WorkerInitResponse.Capabilities;

        if (hostJson is null)
        {
            _logger.LogWarning("No host.json found. The runtime will use a default configuration.");
            return;
        }

        capabilities["host_configuration_json"] = hostJson.Content;
        if (hostJson.Source == HostJsonSource.FunctionAppDirectory)
        {
            _logger.LogInformation("Injected host.json from function app directory '{path}'.", hostJson.Path);
            return;
        }

        _logger.LogInformation("Injected host.json from explicit path '{path}'.", hostJson.Path);
    }

    private HostJsonReadResult? ReadHostJson(string? functionAppDirectory)
    {
        // 1. Try reading from the function app directory.
        if (!string.IsNullOrEmpty(functionAppDirectory))
        {
            string appDirHostJson = Path.Combine(functionAppDirectory, "host.json");
            if (File.Exists(appDirHostJson))
            {
                return new HostJsonReadResult(
                    appDirHostJson,
                    File.ReadAllText(appDirHostJson),
                    HostJsonSource.FunctionAppDirectory);
            }
        }

        // 2. Explicit HostJsonPath option.
        if (!string.IsNullOrEmpty(_options.HostJsonPath) && File.Exists(_options.HostJsonPath))
        {
            return new HostJsonReadResult(
                _options.HostJsonPath,
                File.ReadAllText(_options.HostJsonPath),
                HostJsonSource.ExplicitPath);
        }

        return null;
    }

    private async Task<HostJsonReadResult?> ReadHostJsonAsync(string? functionAppDirectory, CancellationToken cancellationToken)
    {
        // 1. Try reading from the function app directory.
        if (!string.IsNullOrEmpty(functionAppDirectory))
        {
            string appDirHostJson = Path.Combine(functionAppDirectory, "host.json");
            if (File.Exists(appDirHostJson))
            {
                return new HostJsonReadResult(
                    appDirHostJson,
                    await File.ReadAllTextAsync(appDirHostJson, cancellationToken),
                    HostJsonSource.FunctionAppDirectory);
            }
        }

        // 2. Explicit HostJsonPath option.
        if (!string.IsNullOrEmpty(_options.HostJsonPath) && File.Exists(_options.HostJsonPath))
        {
            return new HostJsonReadResult(
                _options.HostJsonPath,
                await File.ReadAllTextAsync(_options.HostJsonPath, cancellationToken),
                HostJsonSource.ExplicitPath);
        }

        return null;
    }

    private void RewriteHttpUri(StreamingMessage message)
    {
        if (message.ContentCase != StreamingMessage.ContentOneofCase.WorkerInitResponse)
        {
            return;
        }

        // Capture the worker's advertised HttpUri (dynamically-chosen port reported by
        // the isolated worker SDK's HttpUriProvider) before we overwrite it. This is what
        // the HTTP forwarding middleware will use as the YARP destination for invocations.
        if (message.WorkerInitResponse.Capabilities.TryGetValue("HttpUri", out var advertisedUri)
            && !string.IsNullOrWhiteSpace(advertisedUri))
        {
            _workerHttpEndpoint = advertisedUri;
            _logger.LogInformation("Captured worker HttpUri '{Uri}' for HTTP invocation forwarding.", advertisedUri);
        }
        else
        {
            // Defense-in-depth: explicitly clear any previously-captured value so the
            // post-rewrite state always reflects the capabilities we just observed.
            // Today there is only one call site (post-merge in SpecializeWorkerAsync),
            // so _workerHttpEndpoint is null here in practice — but if anyone adds a
            // pre-merge capture or re-enables re-specialization, this branch must not
            // silently leave a stale destination in place.
            _workerHttpEndpoint = null;
            _logger.LogWarning("Worker did not advertise an HttpUri capability in WorkerInitResponse. "
                + "HTTP forwarding will require the --worker-http-endpoint override; otherwise requests will return 503.");
        }

        // Overwrite HttpUri with the proxy's endpoint so the runtime routes HTTP requests
        // through this proxy (and we can then forward to the worker's real endpoint).
        message.WorkerInitResponse.Capabilities["HttpUri"] = _options.HttpProxyEndpoint;
        _logger.LogInformation("Set HttpUri capability to {Uri}.", _options.HttpProxyEndpoint);
    }

    internal void TrackFunctionMetadataResponse(FunctionMetadataResponse? response)
    {
        if (response is null)
        {
            return;
        }

        foreach (var functionMetadata in response.FunctionMetadataResults)
        {
            TrackFunctionIdentity(functionMetadata.FunctionId, functionMetadata.Name);
        }
    }

    internal void TrackFunctionLoadRequest(FunctionLoadRequest? request)
    {
        if (request is null)
        {
            return;
        }

        string functionName = ResolveFunctionName(request.FunctionId, request.Metadata?.Name);
        string functionId = NormalizeForLog(request.FunctionId, UnknownFunctionValue);
        _logger.LogInformation("[Runtime] Forwarding FunctionLoadRequest. FunctionName={FunctionName}, FunctionId={FunctionId}",
            functionName,
            functionId);
    }

    internal void LogInvocationRequest(RelaySide side, InvocationRequest? request)
    {
        if (request is null)
        {
            return;
        }

        string functionName = ResolveFunctionName(request.FunctionId);
        string functionId = NormalizeForLog(request.FunctionId, UnknownFunctionValue);
        string invocationId = NormalizeForLog(request.InvocationId);
        string traceParent = NormalizeForLog(request.TraceContext?.TraceParent);

        if (!string.IsNullOrWhiteSpace(request.InvocationId))
        {
            _pendingInvocations[request.InvocationId] = new InvocationCorrelationContext(functionId, functionName, traceParent);
        }

        _logger.LogInformation("[{Side}] Forwarding InvocationRequest. FunctionName={FunctionName}, FunctionId={FunctionId}, InvocationId={InvocationId}, TraceParent={TraceParent}",
            side,
            functionName,
            functionId,
            invocationId,
            traceParent);
    }

    internal void LogInvocationResponse(RelaySide side, InvocationResponse? response)
    {
        if (response is null)
        {
            return;
        }

        string invocationId = NormalizeForLog(response.InvocationId);
        string result = response.Result?.Status.ToString() ?? UnknownFunctionValue;
        string functionId = UnknownFunctionValue;
        string functionName = UnknownFunctionValue;
        string traceParent = NoCorrelationValue;

        if (!string.IsNullOrWhiteSpace(response.InvocationId)
            && _pendingInvocations.TryRemove(response.InvocationId, out var correlation))
        {
            functionId = correlation.FunctionId;
            functionName = correlation.FunctionName;
            traceParent = correlation.TraceParent;
        }

        _logger.LogInformation("[{Side}] Forwarding InvocationResponse. FunctionName={FunctionName}, FunctionId={FunctionId}, InvocationId={InvocationId}, Result={Result}, TraceParent={TraceParent}",
            side,
            functionName,
            functionId,
            invocationId,
            result,
            traceParent);
    }

    private void TrackFunctionIdentity(string? functionId, string? functionName)
    {
        if (string.IsNullOrWhiteSpace(functionId) || string.IsNullOrWhiteSpace(functionName))
        {
            return;
        }

        _functionNamesById[functionId] = functionName;
    }

    private string ResolveFunctionName(string? functionId, string? fallbackFunctionName = null)
    {
        if (!string.IsNullOrWhiteSpace(fallbackFunctionName))
        {
            TrackFunctionIdentity(functionId, fallbackFunctionName);
            return fallbackFunctionName;
        }

        if (!string.IsNullOrWhiteSpace(functionId)
            && _functionNamesById.TryGetValue(functionId, out var functionName))
        {
            return functionName;
        }

        return UnknownFunctionValue;
    }

    private static string NormalizeForLog(string? value, string placeholder = NoCorrelationValue)
        => string.IsNullOrWhiteSpace(value) ? placeholder : value;

    internal async Task RelayAsync(
        IAsyncStreamReader<StreamingMessage> inbound,
        IServerStreamWriter<StreamingMessage> outbound,
        Channel<StreamingMessage> sendChannel,
        Channel<StreamingMessage> receiveChannel,
        RelaySide side,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runtimeReplyChannel = side == RelaySide.Runtime
            ? Channel.CreateUnbounded<StreamingMessage>()
            : null;

        var readTask = ReadInboundAsync(inbound, sendChannel.Writer, runtimeReplyChannel?.Writer ?? receiveChannel.Writer, side, cts.Token);
        var writeTask = side == RelaySide.Runtime
            ? WriteRuntimeOutboundAsync(receiveChannel.Reader, runtimeReplyChannel!.Reader, outbound, cts.Token)
            : WriteOutboundAsync(receiveChannel.Reader, outbound, cts.Token);

        try
        {
            await Task.WhenAny(readTask, writeTask);
        }
        finally
        {
            await cts.CancelAsync();
            runtimeReplyChannel?.Writer.TryComplete();
            try { await readTask; } catch { }
            try { await writeTask; } catch { }
            _pendingInvocations.Clear();
            _logger.LogInformation("[{Side}] stream disconnected", side);
        }
    }

    private async Task WriteRuntimeOutboundAsync(
        ChannelReader<StreamingMessage> sharedReader,
        ChannelReader<StreamingMessage> replyReader,
        IServerStreamWriter<StreamingMessage> outbound,
        CancellationToken cancellationToken)
    {
        await _workerConnected.Task.WaitAsync(cancellationToken);

        var startStream = _cachedStartStream
            ?? throw new InvalidOperationException("Worker connected before StartStream was cached.");

        await outbound.WriteAsync(startStream);
        _logger.LogInformation("WorkerProxy replayed cached StartStream to runtime. WorkerId: {workerId}.", startStream.StartStream?.WorkerId);

        await WriteMergedOutboundAsync(sharedReader, replyReader, outbound, cancellationToken);
    }

    private static async Task WriteMergedOutboundAsync(
        ChannelReader<StreamingMessage> sharedReader,
        ChannelReader<StreamingMessage> replyReader,
        IServerStreamWriter<StreamingMessage> outbound,
        CancellationToken cancellationToken)
    {
        var sharedOpen = true;
        var replyOpen = true;
        Task<bool>? sharedWait = null;
        Task<bool>? replyWait = null;

        while (sharedOpen || replyOpen)
        {
            while (replyOpen && replyReader.TryRead(out var replyMessage))
            {
                await outbound.WriteAsync(replyMessage);
            }

            while (sharedOpen && sharedReader.TryRead(out var sharedMessage))
            {
                await outbound.WriteAsync(sharedMessage);
            }

            if (!sharedOpen && !replyOpen)
            {
                return;
            }

            if (replyOpen && replyWait is null)
            {
                replyWait = replyReader.WaitToReadAsync(cancellationToken).AsTask();
            }

            if (sharedOpen && sharedWait is null)
            {
                sharedWait = sharedReader.WaitToReadAsync(cancellationToken).AsTask();
            }

            if (replyWait is null)
            {
                sharedOpen = await sharedWait!;
                sharedWait = null;
            }
            else if (sharedWait is null)
            {
                replyOpen = await replyWait;
                replyWait = null;
            }
            else
            {
                var completed = await Task.WhenAny(replyWait, sharedWait);
                if (completed == replyWait)
                {
                    replyOpen = await replyWait;
                    replyWait = null;
                }
                else
                {
                    sharedOpen = await sharedWait;
                    sharedWait = null;
                }
            }
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

                if (message.ContentCase == StreamingMessage.ContentOneofCase.FunctionLoadRequest
                    && side == RelaySide.Runtime)
                {
                    TrackFunctionLoadRequest(message.FunctionLoadRequest);
                }
                else if (message.ContentCase == StreamingMessage.ContentOneofCase.InvocationRequest)
                {
                    LogInvocationRequest(side, message.InvocationRequest);
                }
                else if (message.ContentCase == StreamingMessage.ContentOneofCase.InvocationResponse)
                {
                    LogInvocationResponse(side, message.InvocationResponse);
                }

                if (side == RelaySide.Worker)
                {
                    // --- Worker-side interception ---

                    if (message.ContentCase == StreamingMessage.ContentOneofCase.RpcLog)
                    {
                        WriteFunctionsNetHostLogToConsole(message.RpcLog);
                    }

                    // Cache StartStream, then signal connected. Runtime streams replay
                    // this cached message individually so reconnects do not depend on a
                    // one-shot item in the shared _toRuntime channel.
                    if (message.ContentCase == StreamingMessage.ContentOneofCase.StartStream)
                    {
                        _cachedStartStream = message;
                        _workerConnected.TrySetResult();
                        _stateManager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
                        _logger.LogInformation("WorkerProxy cached StartStream from worker. WorkerId: {workerId}.", message.StartStream?.WorkerId);
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
                        var initReplayStart = Stopwatch.GetTimestamp();
                        _logger.LogInformation("WorkerProxy runtime WorkerInitRequest received. Waiting for specialization to complete.");

                        try
                        {
                            await _specializationCompleted.Task.WaitAsync(cancellationToken);
                            _logger.LogInformation("WorkerProxy specialization gate completed for runtime WorkerInitRequest. StepElapsedMilliseconds: {stepElapsedMilliseconds}.", Stopwatch.GetElapsedTime(initReplayStart).TotalMilliseconds);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Worker specialization failed. Cannot serve WorkerInitResponse to runtime.");
                            continue;
                        }

                        if (_cachedWorkerInitResponse is not null)
                        {
                            _logger.LogInformation("WorkerProxy responding to runtime WorkerInitRequest from cache. StepElapsedMilliseconds: {stepElapsedMilliseconds}.", Stopwatch.GetElapsedTime(initReplayStart).TotalMilliseconds);
                            await replyWriter.WriteAsync(_cachedWorkerInitResponse, cancellationToken);
                        }

                        continue;
                    }

                    // Intercept FunctionsMetadataRequest — respond from cache.
                    if (message.ContentCase == StreamingMessage.ContentOneofCase.FunctionsMetadataRequest
                        && _cachedFunctionMetadataResponse is not null)
                    {
                        _logger.LogInformation("WorkerProxy responding to runtime FunctionsMetadataRequest from cache.");
                        await replyWriter.WriteAsync(_cachedFunctionMetadataResponse, cancellationToken);
                        continue;
                    }

                    // Intercept WorkerDrainRequest from runtime — update state machine.
                    if (message.ContentCase == StreamingMessage.ContentOneofCase.WorkerDrainRequest)
                    {
                        _logger.LogInformation("Received WorkerDrainRequest from runtime.");
                        _stateManager.AcceptDrain(DrainReason.RuntimeStopping);
                        continue;
                    }

                    // Intercept WorkerDrainComplete from runtime — update state machine.
                    if (message.ContentCase == StreamingMessage.ContentOneofCase.WorkerDrainComplete)
                    {
                        _logger.LogInformation("Received WorkerDrainComplete from runtime.");
                        _stateManager.UpdatePodStatus(WorkerPodStatus.MarkedForDeletion);

                        // CS-TODO: After drain completes, consider sending WorkerTerminate to the
                        // worker with a grace period, then waiting for the worker to disconnect
                        // before the proxy itself shuts down. Today the platform owns worker
                        // process lifetime (DeletePod), but WorkerTerminate would let the worker
                        // clean up gracefully if it advertises HandlesWorkerTerminateMessage.
                        continue;
                    }
                }

                // Default: relay the message to the other side.
                await forwardWriter.WriteAsync(message, cancellationToken);
            }

            if (side == RelaySide.Worker)
            {
                forwardWriter.TryComplete();
            }

            // If the runtime disconnected while we were draining, treat stream closure
            // as an implicit drain-complete — the runtime may have sent WorkerDrainComplete
            // but the stream was torn down before we could read it.
            if (side == RelaySide.Runtime && _stateManager.CurrentStatus == WorkerPodStatus.Draining)
            {
                _logger.LogInformation("Runtime stream closed while draining. Transitioning to MarkedForDeletion.");
                _stateManager.UpdatePodStatus(WorkerPodStatus.MarkedForDeletion);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (side == RelaySide.Worker)
            {
                forwardWriter.TryComplete(ex);
            }

            throw;
        }
        catch (OperationCanceledException)
        {
            if (side == RelaySide.Worker)
            {
                forwardWriter.TryComplete();
            }
        }
    }

    private static void WriteFunctionsNetHostLogToConsole(RpcLog? rpcLog)
    {
        var message = rpcLog?.Message;
        if (message is null || !message.StartsWith(FunctionsNetHostRpcLogPrefix, StringComparison.Ordinal))
        {
            return;
        }

        Console.WriteLine(message);
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
