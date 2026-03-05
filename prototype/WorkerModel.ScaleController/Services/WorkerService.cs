using WorkerModel.ScaleController.Models;

namespace WorkerModel.ScaleController.Services;

/// <summary>
/// Manages Worker (Sidecar) registrations.
/// Uses in-memory storage for local development.
/// </summary>
public class WorkerService
{
    private readonly InMemoryStore _store;
    private readonly ILogger<WorkerService> _logger;

    public WorkerService(
        InMemoryStore store,
        ILogger<WorkerService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Registers a Worker with the SC.
    /// </summary>
    public Task<WorkerInfo> RegisterAsync(RegisterWorkerRequest request)
    {
        var worker = new WorkerInfo
        {
            Id = request.WorkerId,
            Status = request.IsPlaceholder ? WorkerStatus.Placeholder : WorkerStatus.Specialized,
            SidecarEndpoint = request.SidecarEndpoint,
            LastHeartbeat = DateTime.UtcNow,
            RegisteredAt = DateTime.UtcNow
        };

        _logger.LogInformation("[WorkerService] Registering worker '{WorkerId}' at {Endpoint}",
            request.WorkerId, request.SidecarEndpoint);

        _store.Workers[worker.Id] = worker;
        return Task.FromResult(worker);
    }

    /// <summary>
    /// Gets a Worker by ID.
    /// </summary>
    public Task<WorkerInfo?> GetAsync(string workerId)
    {
        _store.Workers.TryGetValue(workerId, out var worker);
        return Task.FromResult(worker);
    }

    /// <summary>
    /// Gets all Workers.
    /// </summary>
    public Task<List<WorkerInfo>> GetAllAsync()
    {
        return Task.FromResult(_store.Workers.Values.ToList());
    }

    /// <summary>
    /// Gets available placeholder Workers.
    /// </summary>
    public Task<List<WorkerInfo>> GetAvailablePlaceholdersAsync()
    {
        var placeholders = _store.Workers.Values
            .Where(w => w.Status == WorkerStatus.Placeholder)
            .ToList();
        return Task.FromResult(placeholders);
    }

    /// <summary>
    /// Updates Worker heartbeat.
    /// </summary>
    public Task HeartbeatAsync(string workerId)
    {
        if (_store.Workers.TryGetValue(workerId, out var worker))
        {
            worker.LastHeartbeat = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates Worker status after specialization.
    /// </summary>
    public Task UpdateStatusAsync(
        string workerId,
        WorkerStatus status,
        string? applicationId = null,
        string? codeVersion = null,
        string? assignedRuntimeId = null)
    {
        if (_store.Workers.TryGetValue(workerId, out var worker))
        {
            worker.Status = status;
            worker.ApplicationId = applicationId;
            worker.CodeVersion = codeVersion;
            worker.AssignedRuntimeId = assignedRuntimeId;
            worker.LastHeartbeat = DateTime.UtcNow;

            _logger.LogInformation("[WorkerService] Worker '{WorkerId}' status updated to {Status}",
                workerId, status);
        }
        return Task.CompletedTask;
    }
}
