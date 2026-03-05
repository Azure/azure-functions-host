using System.Threading.Channels;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace WorkerModel.Sidecar.Services;

/// <summary>
/// gRPC service that FunctionsNetHost connects to.
/// Acts as a proxy between the worker and the Runtime.
/// Implements the actual FunctionRpc service interface.
/// </summary>
public class SidecarRpcService : FunctionRpc.FunctionRpcBase
{
    private readonly WorkerState _workerState;
    private readonly RuntimeConnectionManager _runtimeConnection;
    private readonly ILogger<SidecarRpcService> _logger;

    public SidecarRpcService(
        WorkerState workerState,
        RuntimeConnectionManager runtimeConnection,
        ILogger<SidecarRpcService> logger)
    {
        _workerState = workerState;
        _runtimeConnection = runtimeConnection;
        _logger = logger;
    }

    /// <summary>
    /// Bidirectional streaming RPC for function invocations.
    /// FunctionsNetHost calls this to communicate with the Runtime.
    /// </summary>
    // Diagnostic counter — visible via /health endpoint
    private static int _eventStreamCallCount;
    private static string? _eventStreamLastError;

    public static int EventStreamCallCount => _eventStreamCallCount;
    public static string? EventStreamLastError => _eventStreamLastError;

    public override async Task EventStream(
        IAsyncStreamReader<StreamingMessage> requestStream,
        IServerStreamWriter<StreamingMessage> responseStream,
        ServerCallContext context)
    {
        Interlocked.Increment(ref _eventStreamCallCount);
        var workerId = _workerState.Context.WorkerId;
        Console.WriteLine($"[SidecarRpc] *** EventStream CALLED (#{_eventStreamCallCount}) - Worker {workerId} connecting ***");
        _logger.LogInformation("[SidecarRpc] Worker {WorkerId} connected (call #{CallCount})", workerId, _eventStreamCallCount);
        // Write diagnostic file for visibility outside Aspire
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "sidecar-eventstream.txt"), $"EventStream called at {DateTime.UtcNow:O} count={_eventStreamCallCount} workerId={workerId}"); } catch { }

        // Store the response stream so SpecializationService can send messages to the worker
        _workerState.SetWorkerStream(responseStream);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);

        try
        {
            // In placeholder mode, we just accept messages but don't forward to Runtime
            // (because we don't have a Runtime connection yet)
            if (_workerState.IsPlaceholder)
            {
                _logger.LogInformation("[SidecarRpc] Worker in placeholder mode - accepting messages but not forwarding");
                await HandlePlaceholderModeAsync(requestStream, responseStream, cts.Token);
            }
            else
            {
                _logger.LogInformation("[SidecarRpc] Worker specialized - proxying to Runtime");
                await HandleSpecializedModeAsync(requestStream, responseStream, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[SidecarRpc] Stream cancelled for worker {WorkerId}", workerId);
        }
        catch (Exception ex)
        {
            _eventStreamLastError = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogError(ex, "[SidecarRpc] Error in event stream for worker {WorkerId}", workerId);
            throw;
        }
    }

    /// <summary>
    /// In placeholder mode, act as the host and drive the initialization handshake.
    /// FunctionsNetHost only sends StartStream on its own — all subsequent requests
    /// (WorkerInitRequest, FunctionsMetadataRequest) must come from the host side.
    ///
    /// Protocol:
    ///   1. Worker → Sidecar: StartStream { WorkerId }
    ///   2. Sidecar → Worker: WorkerInitRequest { HostVersion, FunctionAppDirectory }
    ///   3. Worker → Sidecar: WorkerInitResponse { WorkerVersion, Capabilities }
    ///   4. Sidecar → Worker: FunctionsMetadataRequest { FunctionAppDirectory }
    ///   5. Worker → Sidecar: FunctionMetadataResponse { UseDefaultMetadataIndexing }
    ///   6. Idle — both sides warm, ready for specialization
    /// </summary>
    private async Task HandlePlaceholderModeAsync(
        IAsyncStreamReader<StreamingMessage> requestStream,
        IServerStreamWriter<StreamingMessage> responseStream,
        CancellationToken cancellationToken)
    {
        // ── Step 1: Wait for StartStream from FunctionsNetHost ──
        if (!await requestStream.MoveNext(cancellationToken))
        {
            _logger.LogWarning("[SidecarRpc] Worker disconnected before sending StartStream");
            return;
        }

        var firstMessage = requestStream.Current;
        if (firstMessage.ContentCase != StreamingMessage.ContentOneofCase.StartStream)
        {
            _logger.LogWarning("[SidecarRpc] Expected StartStream, got {ContentCase}", firstMessage.ContentCase);
            return;
        }

        var workerIdFromHost = firstMessage.StartStream.WorkerId;
        _logger.LogInformation("[SidecarRpc] Worker connected with StartStream: WorkerId={WorkerId}", workerIdFromHost);

        // ── Step 2: Send WorkerInitRequest ──
        var scriptRoot = Environment.GetEnvironmentVariable("AzureWebJobsScriptRoot") ?? "/home/site/wwwroot";
        var initRequest = new StreamingMessage
        {
            RequestId = Guid.NewGuid().ToString(),
            WorkerInitRequest = new WorkerInitRequest
            {
                HostVersion = "4.0.0-prototype",
                FunctionAppDirectory = scriptRoot,
                WorkerDirectory = scriptRoot
            }
        };

        // Add host capabilities that FunctionsNetHost/worker SDK may expect
        initRequest.WorkerInitRequest.Capabilities.Add("RawHttpBodyBytes", "true");
        initRequest.WorkerInitRequest.Capabilities.Add("RpcHttpTriggerMetadataRemoved", "true");
        initRequest.WorkerInitRequest.Capabilities.Add("RpcHttpBodyOnly", "true");
        initRequest.WorkerInitRequest.Capabilities.Add("UseNullableValueDictionaryForHttp", "true");
        initRequest.WorkerInitRequest.Capabilities.Add("TypedDataCollection", "true");
        initRequest.WorkerInitRequest.Capabilities.Add("WorkerStatus", "true");
        initRequest.WorkerInitRequest.Capabilities.Add("HandlesWorkerTerminateMessage", "true");
        initRequest.WorkerInitRequest.Capabilities.Add("HandlesInvocationCancelMessage", "true");

        _logger.LogInformation("[SidecarRpc] Sending WorkerInitRequest (FunctionAppDirectory={Dir})", scriptRoot);
        await responseStream.WriteAsync(initRequest, cancellationToken);

        // ── Step 3: Wait for WorkerInitResponse ──
        if (!await requestStream.MoveNext(cancellationToken))
        {
            _logger.LogWarning("[SidecarRpc] Worker disconnected before sending WorkerInitResponse");
            return;
        }

        var initResponseMsg = requestStream.Current;
        if (initResponseMsg.ContentCase == StreamingMessage.ContentOneofCase.WorkerInitResponse)
        {
            var initResp = initResponseMsg.WorkerInitResponse;
            _logger.LogInformation(
                "[SidecarRpc] Received WorkerInitResponse: Version={Version}, Status={Status}, Capabilities=[{Caps}]",
                initResp.WorkerVersion,
                initResp.Result?.Status,
                string.Join(", ", initResp.Capabilities.Select(kv => $"{kv.Key}={kv.Value}")));

            // Store for WorkerConnect message later
            _workerState.SetWorkerInitData(initResp);
        }
        else
        {
            _logger.LogWarning("[SidecarRpc] Expected WorkerInitResponse, got {ContentCase}", initResponseMsg.ContentCase);
        }

        // ── Step 4: Send FunctionsMetadataRequest (placeholder: no functions) ──
        var metadataRequest = new StreamingMessage
        {
            RequestId = Guid.NewGuid().ToString(),
            FunctionsMetadataRequest = new FunctionsMetadataRequest
            {
                FunctionAppDirectory = scriptRoot
            }
        };

        _logger.LogInformation("[SidecarRpc] Sending FunctionsMetadataRequest");
        await responseStream.WriteAsync(metadataRequest, cancellationToken);

        // ── Step 5: Wait for FunctionMetadataResponse ──
        if (!await requestStream.MoveNext(cancellationToken))
        {
            _logger.LogWarning("[SidecarRpc] Worker disconnected before sending FunctionMetadataResponse");
            return;
        }

        var metaResponseMsg = requestStream.Current;
        if (metaResponseMsg.ContentCase == StreamingMessage.ContentOneofCase.FunctionMetadataResponse)
        {
            var metaResp = metaResponseMsg.FunctionMetadataResponse;
            _logger.LogInformation(
                "[SidecarRpc] Received FunctionMetadataResponse: Status={Status}, UseDefaultIndexing={Default}, FunctionCount={Count}",
                metaResp.Result?.Status,
                metaResp.UseDefaultMetadataIndexing,
                metaResp.FunctionMetadataResults.Count);

            // Store for WorkerConnect message later
            _workerState.SetFunctionMetadata(metaResp);
        }
        else
        {
            _logger.LogWarning("[SidecarRpc] Expected FunctionMetadataResponse, got {ContentCase}", metaResponseMsg.ContentCase);
        }

        // ── Step 6: Warm and idle — keep the stream alive ──
        _logger.LogInformation("[SidecarRpc] ✅ Placeholder warm-up complete. Worker is idle and ready for specialization.");
        _workerState.SetPlaceholderReady();

        // Keep reading messages (heartbeats, status requests, reload responses, etc.) until specialized
        while (await requestStream.MoveNext(cancellationToken))
        {
            var message = requestStream.Current;
            _logger.LogDebug("[SidecarRpc] Placeholder received: {ContentCase}", message.ContentCase);

            switch (message.ContentCase)
            {
                case StreamingMessage.ContentOneofCase.WorkerHeartbeat:
                    _logger.LogDebug("[SidecarRpc] Received heartbeat");
                    break;

                case StreamingMessage.ContentOneofCase.WorkerStatusRequest:
                    var statusResponse = new StreamingMessage
                    {
                        RequestId = message.RequestId,
                        WorkerStatusResponse = new WorkerStatusResponse()
                    };
                    await responseStream.WriteAsync(statusResponse, cancellationToken);
                    break;

                case StreamingMessage.ContentOneofCase.FunctionEnvironmentReloadResponse:
                    // Worker finished reloading after specialization — capture refreshed capabilities
                    // (e.g. HttpUri from Worker.Extensions.Http.AspNetCore) and worker metadata.
                    var reloadResp = message.FunctionEnvironmentReloadResponse;
                    _logger.LogInformation(
                        "[SidecarRpc] Received FunctionEnvironmentReloadResponse: Status={Status}, Capabilities=[{Caps}]",
                        reloadResp.Result?.Status,
                        string.Join(", ", reloadResp.Capabilities.Select(kv => $"{kv.Key}={kv.Value}")));
                    _workerState.ApplyReloadResponseData(reloadResp);
                    _workerState.CompleteReloadResponse(message);
                    
                    // Wait for SpecializationService to complete (includes Runtime connection)
                    _logger.LogInformation("[SidecarRpc] Waiting for specialization to complete...");
                    await _workerState.WaitForSpecializationCompleteAsync(cancellationToken);
                    
                    _logger.LogInformation("[SidecarRpc] Transitioning to specialized mode...");

                    try
                    {
                        // Re-index functions now that the worker has the real app code
                        await RefreshFunctionMetadataAsync(responseStream, requestStream, cancellationToken);
                        
                        // Send WorkerConnect to Runtime (all worker + function info in one message)
                        await SendWorkerConnectToRuntimeAsync(cancellationToken);

                        // Wait for the Runtime to acknowledge WorkerConnect. The Runtime sends
                        // WorkerConnectResponse after the JobHost has started, HTTP routes are
                        // registered, and FunctionLoadRequests have been dispatched. Without this
                        // wait, the ScaleController may forward the first HTTP request before
                        // routes exist, causing a 404.
                        await WaitForWorkerConnectResponseAsync(cancellationToken);

                        // Signal that the Runtime is now ready to serve HTTP traffic.
                        // SpecializationService (which holds the /assign request) will now return 200.
                        _workerState.SignalRuntimeReady();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[SidecarRpc] Failed during WorkerConnect / Runtime readiness flow");
                        _workerState.SignalRuntimeFailed(ex);
                        throw;
                    }

                    // NOTE: We do NOT send FunctionLoadRequests here. The Runtime will send its own
                    // FunctionLoadRequests (after processing WorkerConnect) which flow through the
                    // relay to the worker. This ensures that FunctionLoadRequest.FunctionId and
                    // InvocationRequest.FunctionId come from the same source (the Runtime).
                    
                    // Enter relay mode
                    await HandleSpecializedModeAsync(requestStream, responseStream, cancellationToken);
                    return; // Exit placeholder mode

                default:
                    _logger.LogDebug("[SidecarRpc] Ignoring message in placeholder mode: {ContentCase}", message.ContentCase);
                    break;
            }
        }
    }

    /// <summary>
    /// After specialization reload, asks the worker to re-index functions using the
    /// real app code (the placeholder warm-up only saw an empty script root).
    /// Updates WorkerState with the fresh metadata before WorkerConnect is sent.
    /// </summary>
    private async Task RefreshFunctionMetadataAsync(
        IServerStreamWriter<StreamingMessage> responseStream,
        IAsyncStreamReader<StreamingMessage> requestStream,
        CancellationToken cancellationToken)
    {
        var scriptRoot = Environment.GetEnvironmentVariable("AzureWebJobsScriptRoot") ?? "/home/site/wwwroot";

        var metadataRequest = new StreamingMessage
        {
            RequestId = Guid.NewGuid().ToString(),
            FunctionsMetadataRequest = new FunctionsMetadataRequest
            {
                FunctionAppDirectory = scriptRoot
            }
        };

        _logger.LogInformation("[SidecarRpc] Sending post-specialization FunctionsMetadataRequest (scriptRoot={ScriptRoot})", scriptRoot);
        await responseStream.WriteAsync(metadataRequest, cancellationToken);

        // Read the response — may need to skip non-metadata messages (e.g. heartbeats)
        while (await requestStream.MoveNext(cancellationToken))
        {
            var msg = requestStream.Current;

            if (msg.ContentCase == StreamingMessage.ContentOneofCase.FunctionMetadataResponse)
            {
                var metaResp = msg.FunctionMetadataResponse;
                _logger.LogInformation(
                    "[SidecarRpc] Post-specialization FunctionMetadataResponse: Status={Status}, UseDefaultIndexing={Default}, FunctionCount={Count}",
                    metaResp.Result?.Status,
                    metaResp.UseDefaultMetadataIndexing,
                    metaResp.FunctionMetadataResults.Count);

                // Overwrite the stale placeholder metadata
                _workerState.SetFunctionMetadata(metaResp);
                return;
            }

            _logger.LogDebug("[SidecarRpc] Skipping {ContentCase} while waiting for FunctionMetadataResponse", msg.ContentCase);
        }

        _logger.LogWarning("[SidecarRpc] Worker disconnected before sending post-specialization FunctionMetadataResponse");
    }

    /// <summary>
    /// Sends a WorkerConnect message to the Runtime to establish the worker connection.
    /// This replaces the old multi-step handshake (StartStream + WorkerInit + FunctionMetadata)
    /// with a single message containing all worker and function info.
    /// </summary>
    private async Task SendWorkerConnectToRuntimeAsync(CancellationToken cancellationToken)
    {
        var context = _workerState.Context;

        var workerConnect = new WorkerConnect
        {
            WorkerId = context.WorkerId,
            Language = context.Language,
            LanguageVersion = context.LanguageVersion,
            UseDefaultMetadataIndexing = _workerState.UseDefaultMetadataIndexing,
        };

        if (_workerState.WorkerCapabilities is not null)
        {
            workerConnect.WorkerCapabilities.Add(_workerState.WorkerCapabilities);
        }

        if (_workerState.WorkerMetadata is not null)
        {
            workerConnect.WorkerMetadata = _workerState.WorkerMetadata;
        }

        if (_workerState.FunctionMetadata is not null)
        {
            workerConnect.FunctionMetadata.AddRange(_workerState.FunctionMetadata);
        }

        var message = new StreamingMessage
        {
            RequestId = Guid.NewGuid().ToString(),
            WorkerConnect = workerConnect
        };

        _logger.LogInformation(
            "[SidecarRpc] Sending WorkerConnect to Runtime: WorkerId={WorkerId}, "
            + "Language={Language}/{LangVersion}, Capabilities={CapCount}, Functions={FuncCount}",
            workerConnect.WorkerId,
            workerConnect.Language,
            workerConnect.LanguageVersion,
            workerConnect.WorkerCapabilities.Count,
            workerConnect.FunctionMetadata.Count);

        await _runtimeConnection.SendToRuntimeAsync(message, cancellationToken);
    }

    /// <summary>
    /// Waits for the Runtime to send a WorkerConnectResponse after receiving WorkerConnect.
    /// This ensures the Runtime has started the JobHost, registered HTTP routes, and dispatched
    /// FunctionLoadRequests before the ScaleController forwards HTTP traffic.
    /// Any other messages received while waiting (e.g. FunctionLoadRequests) are forwarded
    /// to the worker via the stored gRPC stream.
    /// </summary>
    private async Task WaitForWorkerConnectResponseAsync(CancellationToken cancellationToken)
    {
        var fromRuntime = _runtimeConnection.FromRuntime;
        if (fromRuntime is null)
        {
            _logger.LogWarning("[SidecarRpc] Cannot wait for WorkerConnectResponse — not connected to Runtime");
            return;
        }

        _logger.LogInformation("[SidecarRpc] Waiting for WorkerConnectResponse from Runtime...");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await foreach (var message in fromRuntime.ReadAllAsync(linked.Token))
            {
                if (message.ContentCase == StreamingMessage.ContentOneofCase.WorkerConnectResponse)
                {
                    var status = message.WorkerConnectResponse?.Result?.Status;
                    _logger.LogInformation("[SidecarRpc] Received WorkerConnectResponse: Status={Status}", status);

                    if (status != StatusResult.Types.Status.Success)
                    {
                        var error = message.WorkerConnectResponse?.Result?.Exception?.Message ?? "Unknown error";
                        _logger.LogError("[SidecarRpc] Runtime failed to process WorkerConnect: {Error}", error);
                        throw new InvalidOperationException($"Runtime failed to process WorkerConnect: {error}");
                    }

                    return;
                }

                // Forward other messages (e.g. FunctionLoadRequests) to the worker while waiting
                _logger.LogDebug("[SidecarRpc] Forwarding {ContentCase} to worker while waiting for WorkerConnectResponse",
                    message.ContentCase);
                await _workerState.SendToWorkerAsync(message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("[SidecarRpc] Timed out waiting for WorkerConnectResponse from Runtime (60s)");
            throw new TimeoutException("Runtime did not send WorkerConnectResponse within 60 seconds");
        }
    }

    /// <summary>
    /// In specialized mode, proxy messages between worker and Runtime.
    /// </summary>
    private async Task HandleSpecializedModeAsync(
        IAsyncStreamReader<StreamingMessage> requestStream,
        IServerStreamWriter<StreamingMessage> responseStream,
        CancellationToken cancellationToken)
    {
        if (!_runtimeConnection.IsConnected)
        {
            _logger.LogError("[SidecarRpc] Specialized but not connected to Runtime!");
            throw new InvalidOperationException("Worker specialized but not connected to Runtime");
        }

        // Start two tasks: one to relay worker->runtime, one to relay runtime->worker
        var workerToRuntime = RelayWorkerToRuntimeAsync(requestStream, cancellationToken);
        var runtimeToWorker = RelayRuntimeToWorkerAsync(responseStream, cancellationToken);

        await Task.WhenAll(workerToRuntime, runtimeToWorker);
    }

    private async Task RelayWorkerToRuntimeAsync(
        IAsyncStreamReader<StreamingMessage> workerStream,
        CancellationToken cancellationToken)
    {
        await foreach (var message in workerStream.ReadAllAsync(cancellationToken))
        {
            _logger.LogDebug("[SidecarRpc] Relaying worker->runtime: {ContentCase}", message.ContentCase);

            // StartStream was already sent by us when transitioning to specialized mode
            // If worker sends another StartStream (shouldn't happen), skip it
            if (message.ContentCase == StreamingMessage.ContentOneofCase.StartStream)
            {
                _logger.LogDebug("[SidecarRpc] Ignoring StartStream from worker (already sent to Runtime)");
                continue;
            }
            
            await _runtimeConnection.SendToRuntimeAsync(message, cancellationToken);
        }
    }

    private async Task RelayRuntimeToWorkerAsync(
        IServerStreamWriter<StreamingMessage> workerStream,
        CancellationToken cancellationToken)
    {
        var fromRuntime = _runtimeConnection.FromRuntime;
        if (fromRuntime is null)
        {
            return;
        }

        await foreach (var message in fromRuntime.ReadAllAsync(cancellationToken))
        {
            // Filter messages the worker doesn't understand or that are no longer needed
            if (message.ContentCase == StreamingMessage.ContentOneofCase.WorkerConnectResponse)
            {
                _logger.LogInformation("[SidecarRpc] Received WorkerConnectResponse from Runtime (not relaying to worker)");
                continue;
            }

            if (message.ContentCase == StreamingMessage.ContentOneofCase.WorkerInitRequest)
            {
                _logger.LogDebug("[SidecarRpc] Filtering WorkerInitRequest from relay (already handled during placeholder)");
                continue;
            }

            _logger.LogDebug("[SidecarRpc] Relaying runtime->worker: {ContentCase}", message.ContentCase);
            await workerStream.WriteAsync(message, cancellationToken);
        }
    }
}
