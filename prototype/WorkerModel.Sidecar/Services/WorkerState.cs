using Grpc.Core;
using WorkerModel.Contracts;
using GrpcMessages = Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace WorkerModel.Sidecar.Services;

/// <summary>
/// Tracks the current state of this worker instance.
/// </summary>
public class WorkerState
{
    private readonly object _lock = new();
    private WorkerContext _context;
    private IServerStreamWriter<GrpcMessages.StreamingMessage>? _workerResponseStream;
    private TaskCompletionSource<GrpcMessages.StreamingMessage>? _pendingReloadResponse;

    public WorkerState()
    {
        var workerId = Environment.GetEnvironmentVariable("SIDECAR_WORKER_ID") 
            ?? Environment.GetEnvironmentVariable("WORKER_ID") 
            ?? $"worker-{Guid.NewGuid():N}";
        var language = Environment.GetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME") ?? "dotnet-isolated";
        var languageVersion = Environment.GetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME_VERSION") ?? "8.0";

        _context = WorkerContext.CreatePlaceholder(workerId, language, languageVersion);
    }

    /// <summary>
    /// Stores the worker's gRPC response stream for sending messages.
    /// </summary>
    public void SetWorkerStream(IServerStreamWriter<GrpcMessages.StreamingMessage> responseStream)
    {
        lock (_lock)
        {
            _workerResponseStream = responseStream;
        }
    }

    /// <summary>
    /// Sends a message directly to the worker via the stored gRPC stream.
    /// </summary>
    public async Task SendToWorkerAsync(GrpcMessages.StreamingMessage message, CancellationToken cancellationToken)
    {
        IServerStreamWriter<GrpcMessages.StreamingMessage>? stream;
        lock (_lock)
        {
            stream = _workerResponseStream;
        }

        if (stream is null)
        {
            throw new InvalidOperationException("Worker stream not available");
        }

        await stream.WriteAsync(message, cancellationToken);
    }

    /// <summary>
    /// Sets up expectation for a FunctionEnvironmentReloadResponse.
    /// </summary>
    public void ExpectReloadResponse()
    {
        lock (_lock)
        {
            _pendingReloadResponse = new TaskCompletionSource<GrpcMessages.StreamingMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>
    /// Waits for the FunctionEnvironmentReloadResponse from the worker.
    /// </summary>
    public async Task<GrpcMessages.StreamingMessage> WaitForReloadResponseAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<GrpcMessages.StreamingMessage>? tcs;
        lock (_lock)
        {
            tcs = _pendingReloadResponse;
        }

        if (tcs is null)
        {
            throw new InvalidOperationException("Not expecting reload response");
        }

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task;
    }

    /// <summary>
    /// Called when a FunctionEnvironmentReloadResponse is received from the worker.
    /// </summary>
    public void CompleteReloadResponse(GrpcMessages.StreamingMessage message)
    {
        lock (_lock)
        {
            _pendingReloadResponse?.TrySetResult(message);
            _pendingReloadResponse = null;
        }
    }

    private TaskCompletionSource<bool>? _specializationComplete;
    private TaskCompletionSource<bool>? _runtimeReady;

    /// <summary>
    /// Sets up a signal for when specialization is fully complete (Runtime connected).
    /// </summary>
    public void ExpectSpecializationComplete()
    {
        lock (_lock)
        {
            _specializationComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _runtimeReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>
    /// Signals that specialization is complete and Runtime is connected.
    /// SidecarRpcService will then send WorkerConnect and wait for WorkerConnectResponse.
    /// </summary>
    public void SignalSpecializationComplete()
    {
        lock (_lock)
        {
            _specializationComplete?.TrySetResult(true);
        }
    }

    /// <summary>
    /// Signals that the Runtime has processed WorkerConnect and is ready to accept
    /// HTTP traffic (JobHost started, routes registered, FunctionLoadRequests sent).
    /// </summary>
    public void SignalRuntimeReady()
    {
        lock (_lock)
        {
            _runtimeReady?.TrySetResult(true);
        }
    }

    /// <summary>
    /// Signals that the Runtime failed to process WorkerConnect.
    /// </summary>
    public void SignalRuntimeFailed(Exception exception)
    {
        lock (_lock)
        {
            _runtimeReady?.TrySetException(exception);
        }
    }

    /// <summary>
    /// Waits for specialization to complete (including Runtime connection).
    /// </summary>
    public async Task WaitForSpecializationCompleteAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? tcs;
        lock (_lock)
        {
            tcs = _specializationComplete;
        }

        if (tcs is null)
        {
            // Already specialized or not expected
            if (!IsPlaceholder)
            {
                return;
            }
            throw new InvalidOperationException("Not expecting specialization");
        }

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        await tcs.Task;
    }

    /// <summary>
    /// Waits for the Runtime to be ready (WorkerConnect processed, routes registered).
    /// Called by SpecializationService so /assign doesn't return until traffic can be served.
    /// </summary>
    public async Task WaitForRuntimeReadyAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? tcs;
        lock (_lock)
        {
            tcs = _runtimeReady;
        }

        if (tcs is null)
        {
            if (!IsPlaceholder)
            {
                return;
            }
            throw new InvalidOperationException("Not expecting runtime ready signal");
        }

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        await tcs.Task;
    }

    /// <summary>
    /// Gets the current worker context.
    /// </summary>
    public WorkerContext Context
    {
        get
        {
            lock (_lock)
            {
                return _context;
            }
        }
    }

    /// <summary>
    /// Gets whether this worker is still in placeholder mode.
    /// </summary>
    public bool IsPlaceholder => Context.IsPlaceholder;

    /// <summary>
    /// Gets whether the placeholder warm-up handshake is complete
    /// (StartStream → WorkerInitRequest/Response → FunctionsMetadataRequest/Response).
    /// </summary>
    public bool IsPlaceholderReady { get; private set; }

    /// <summary>
    /// Gets the assigned Runtime endpoint (null if placeholder).
    /// </summary>
    public string? RuntimeEndpoint { get; private set; }

    // --- Data captured during placeholder warm-up (for WorkerConnect) ---

    /// <summary>
    /// Worker capabilities advertised during WorkerInitResponse.
    /// </summary>
    public IDictionary<string, string>? WorkerCapabilities { get; private set; }

    /// <summary>
    /// Worker metadata from WorkerInitResponse (version, bitness, etc.).
    /// </summary>
    public GrpcMessages.WorkerMetadata? WorkerMetadata { get; private set; }

    /// <summary>
    /// Function metadata discovered during placeholder FunctionMetadataResponse.
    /// </summary>
    public IReadOnlyList<GrpcMessages.RpcFunctionMetadata>? FunctionMetadata { get; private set; }

    /// <summary>
    /// Whether the worker uses default metadata indexing.
    /// </summary>
    public bool UseDefaultMetadataIndexing { get; private set; }

    /// <summary>
    /// Stores the worker init data captured during placeholder warm-up.
    /// </summary>
    public void SetWorkerInitData(GrpcMessages.WorkerInitResponse initResponse)
    {
        WorkerCapabilities = new Dictionary<string, string>(initResponse.Capabilities);
        WorkerMetadata = initResponse.WorkerMetadata;
    }

    /// <summary>
    /// Updates capabilities and metadata from a FunctionEnvironmentReloadResponse.
    /// After specialization, the worker may advertise new capabilities (e.g. HttpUri)
    /// that were not present during the placeholder warm-up.
    /// </summary>
    public void ApplyReloadResponseData(GrpcMessages.FunctionEnvironmentReloadResponse reloadResponse)
    {
        // Update worker metadata if provided
        if (reloadResponse.WorkerMetadata is not null)
        {
            WorkerMetadata = reloadResponse.WorkerMetadata;
        }

        // Apply capabilities using the strategy specified by the worker
        if (reloadResponse.Capabilities.Count > 0)
        {
            var strategy = reloadResponse.CapabilitiesUpdateStrategy;

            if (strategy == GrpcMessages.FunctionEnvironmentReloadResponse.Types.CapabilitiesUpdateStrategy.Replace)
            {
                // Replace: discard old capabilities entirely
                WorkerCapabilities = new Dictionary<string, string>(reloadResponse.Capabilities);
            }
            else
            {
                // Merge (default): overwrite existing keys and add new ones
                WorkerCapabilities ??= new Dictionary<string, string>();
                foreach (var kvp in reloadResponse.Capabilities)
                {
                    WorkerCapabilities[kvp.Key] = kvp.Value;
                }
            }
        }
    }

    /// <summary>
    /// Stores the function metadata captured during placeholder warm-up.
    /// </summary>
    public void SetFunctionMetadata(GrpcMessages.FunctionMetadataResponse metadataResponse)
    {
        FunctionMetadata = metadataResponse.FunctionMetadataResults.ToList().AsReadOnly();
        UseDefaultMetadataIndexing = metadataResponse.UseDefaultMetadataIndexing;
    }

    /// <summary>
    /// Marks the placeholder as warm and ready for specialization.
    /// Called after the gRPC init handshake completes successfully.
    /// </summary>
    public void SetPlaceholderReady()
    {
        IsPlaceholderReady = true;
        Console.WriteLine("[WorkerState] Placeholder warm-up complete — ready for specialization");
    }

    /// <summary>
    /// Specializes this worker with an application assignment.
    /// </summary>
    public void Specialize(ApplicationDefinition application, string runtimeEndpoint)
    {
        lock (_lock)
        {
            if (!_context.IsPlaceholder)
            {
                throw new InvalidOperationException("Worker is already specialized");
            }

            _context = _context.Specialize(application);
            RuntimeEndpoint = runtimeEndpoint;

            Console.WriteLine($"[WorkerState] Specialized to app '{application.ApplicationId}' (code version: {application.CodeVersion})");
            Console.WriteLine($"[WorkerState] Assigned to runtime: {runtimeEndpoint}");
        }
    }
}
