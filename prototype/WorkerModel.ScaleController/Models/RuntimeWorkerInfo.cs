namespace WorkerModel.ScaleController.Models;

/// <summary>
/// Runtime instance metadata stored in Cosmos DB.
/// </summary>
public class RuntimeInfo
{
    public string Id { get; set; } = string.Empty;
    public string PartitionKey => Id;
    public RuntimeStatus Status { get; set; } = RuntimeStatus.Placeholder;
    public string? ApplicationId { get; set; }
    public string GrpcEndpoint { get; set; } = string.Empty;
    public string HttpEndpoint { get; set; } = string.Empty;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

public enum RuntimeStatus
{
    Placeholder,
    Specializing,
    Specialized
}

/// <summary>
/// Request to register a Runtime with the SC.
/// </summary>
public class RegisterRuntimeRequest
{
    public string RuntimeId { get; set; } = string.Empty;
    public string GrpcEndpoint { get; set; } = string.Empty;
    public string HttpEndpoint { get; set; } = string.Empty;
}

/// <summary>
/// Worker (Sidecar) instance metadata stored in Cosmos DB.
/// </summary>
public class WorkerInfo
{
    public string Id { get; set; } = string.Empty;
    public string PartitionKey => Id;
    public WorkerStatus Status { get; set; } = WorkerStatus.Placeholder;
    public string? ApplicationId { get; set; }
    public string? CodeVersion { get; set; }
    public string? AssignedRuntimeId { get; set; }
    public string SidecarEndpoint { get; set; } = string.Empty;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

public enum WorkerStatus
{
    Placeholder,
    Specializing,
    Specialized
}

/// <summary>
/// Request to register a Worker with the SC.
/// </summary>
public class RegisterWorkerRequest
{
    public string WorkerId { get; set; } = string.Empty;
    public string SidecarEndpoint { get; set; } = string.Empty;
    public bool IsPlaceholder { get; set; } = true;
}

/// <summary>
/// Request to trigger specialization.
/// </summary>
public class SpecializeWorkerRequest
{
    public string AppId { get; set; } = string.Empty;
}

/// <summary>
/// Current status of the system.
/// </summary>
public class SystemStatus
{
    public List<ApplicationInfo> Applications { get; set; } = new();
    public List<RuntimeInfo> Runtimes { get; set; } = new();
    public List<WorkerInfo> Workers { get; set; } = new();
}

/// <summary>
/// Optional heartbeat request body for RuntimeSidecar.
/// </summary>
public class HeartbeatRequest
{
    /// <summary>
    /// WebHost endpoint, sent by RuntimeSidecar after the WebHost has started.
    /// </summary>
    public string? WebHostEndpoint { get; set; }
}
