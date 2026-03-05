using WorkerModel.ScaleController.Models;

namespace WorkerModel.ScaleController.Services;

/// <summary>
/// Manages Runtime registrations.
/// Uses in-memory storage for local development.
/// </summary>
public class RuntimeService
{
    private readonly InMemoryStore _store;
    private readonly ILogger<RuntimeService> _logger;

    public RuntimeService(
        InMemoryStore store,
        ILogger<RuntimeService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Registers a Runtime with the SC.
    /// </summary>
    public Task<RuntimeInfo> RegisterAsync(RegisterRuntimeRequest request)
    {
        var runtime = new RuntimeInfo
        {
            Id = request.RuntimeId,
            Status = RuntimeStatus.Placeholder,
            GrpcEndpoint = request.GrpcEndpoint,
            HttpEndpoint = request.HttpEndpoint,
            LastHeartbeat = DateTime.UtcNow,
            RegisteredAt = DateTime.UtcNow
        };

        _logger.LogInformation("[RuntimeService] Registering runtime '{RuntimeId}' at {Endpoint}",
            request.RuntimeId, request.GrpcEndpoint);

        _store.Runtimes[runtime.Id] = runtime;
        return Task.FromResult(runtime);
    }

    /// <summary>
    /// Gets a Runtime by ID.
    /// </summary>
    public Task<RuntimeInfo?> GetAsync(string runtimeId)
    {
        _store.Runtimes.TryGetValue(runtimeId, out var runtime);
        return Task.FromResult(runtime);
    }

    /// <summary>
    /// Gets all Runtimes.
    /// </summary>
    public Task<List<RuntimeInfo>> GetAllAsync()
    {
        return Task.FromResult(_store.Runtimes.Values.ToList());
    }

    /// <summary>
    /// Gets available placeholder Runtimes.
    /// </summary>
    public Task<List<RuntimeInfo>> GetAvailablePlaceholdersAsync()
    {
        var placeholders = _store.Runtimes.Values
            .Where(r => r.Status == RuntimeStatus.Placeholder)
            .ToList();
        return Task.FromResult(placeholders);
    }

    /// <summary>
    /// Gets a Runtime that is specialized for a specific application.
    /// Returns null if no runtime is specialized for this app.
    /// </summary>
    public Task<RuntimeInfo?> GetSpecializedForAppAsync(string appId)
    {
        var runtime = _store.Runtimes.Values
            .FirstOrDefault(r => r.Status == RuntimeStatus.Specialized && 
                                 string.Equals(r.ApplicationId, appId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(runtime);
    }

    /// <summary>
    /// Updates Runtime heartbeat.
    /// </summary>
    public Task HeartbeatAsync(string runtimeId)
    {
        if (_store.Runtimes.TryGetValue(runtimeId, out var runtime))
        {
            runtime.LastHeartbeat = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates Runtime status.
    /// </summary>
    public Task UpdateStatusAsync(string runtimeId, RuntimeStatus status, string? applicationId = null)
    {
        if (_store.Runtimes.TryGetValue(runtimeId, out var runtime))
        {
            runtime.Status = status;
            runtime.ApplicationId = applicationId;
            runtime.LastHeartbeat = DateTime.UtcNow;
            
            _logger.LogInformation("[RuntimeService] Runtime '{RuntimeId}' status updated to {Status}",
                runtimeId, status);
        }
        return Task.CompletedTask;
    }
}
