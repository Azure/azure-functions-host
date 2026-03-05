namespace WorkerModel.ScaleController.Models;

/// <summary>
/// Application metadata stored in Cosmos DB.
/// </summary>
public class ApplicationInfo
{
    public string Id { get; set; } = string.Empty;
    public string PartitionKey => Id;
    public string DisplayName { get; set; } = string.Empty;
    public string Language { get; set; } = "dotnet-isolated";
    public string LanguageVersion { get; set; } = "8.0";
    public string MetadataVersion { get; set; } = "1";
    public string CodeVersion { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
    public Dictionary<string, string> Environment { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Request to create a new application.
/// </summary>
public class CreateApplicationRequest
{
    public string AppId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Language { get; set; } = "dotnet-isolated";
    public string LanguageVersion { get; set; } = "8.0";
}

/// <summary>
/// Response after deploying app code.
/// </summary>
public class DeploymentResponse
{
    public string AppId { get; set; } = string.Empty;
    public string CodeVersion { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
    public DateTime DeployedAt { get; set; } = DateTime.UtcNow;
}
