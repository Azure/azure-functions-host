namespace WorkerModel.Contracts;

/// <summary>
/// Context provided by Scale Controller when assigning a worker to a runtime.
/// Mirrors the existing HostAssignmentContext from WebHost.
/// </summary>
public class HostAssignmentContext
{
    /// <summary>
    /// Name of the site/function app.
    /// </summary>
    public string SiteName { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier for the site.
    /// </summary>
    public string? SiteId { get; set; }

    /// <summary>
    /// Environment variables to set on the worker.
    /// Includes app settings like AzureWebJobsScriptRoot, WEBSITE_RUN_FROM_PACKAGE, etc.
    /// </summary>
    public Dictionary<string, string> Environment { get; set; } = new();

    /// <summary>
    /// MSI endpoint for managed identity (optional for prototype).
    /// </summary>
    public string? MsiEndpoint { get; set; }

    /// <summary>
    /// MSI secret for managed identity (optional for prototype).
    /// </summary>
    public string? MsiSecret { get; set; }
}

/// <summary>
/// Request sent by Scale Controller to Worker Sidecar to assign it to a runtime.
/// </summary>
public class WorkerAssignmentRequest
{
    /// <summary>
    /// The gRPC endpoint of the runtime this worker should connect to.
    /// Only provided at specialization time (late-binding).
    /// </summary>
    public string RuntimeEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// The host assignment context with app settings and package URL.
    /// </summary>
    public HostAssignmentContext HostAssignmentContext { get; set; } = new();
}
