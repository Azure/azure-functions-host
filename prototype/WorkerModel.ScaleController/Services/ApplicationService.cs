using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using WorkerModel.ScaleController.Models;

namespace WorkerModel.ScaleController.Services;

/// <summary>
/// Manages application metadata and deployment.
/// Uses in-memory for metadata, Azure Blob Storage for packages.
/// </summary>
public class ApplicationService
{
    private readonly InMemoryStore _store;
    private readonly BlobServiceClient _blobClient;
    private readonly IConfiguration _config;
    private readonly ILogger<ApplicationService> _logger;

    public ApplicationService(
        InMemoryStore store,
        BlobServiceClient blobClient,
        IConfiguration config,
        ILogger<ApplicationService> logger)
    {
        _store = store;
        _blobClient = blobClient;
        _config = config;
        _logger = logger;
    }

    private BlobContainerClient PackagesContainer =>
        _blobClient.GetBlobContainerClient(_config["ScaleController:BlobContainer"] ?? "function-apps");

    /// <summary>
    /// Creates a new application registration.
    /// </summary>
    public Task<ApplicationInfo> CreateAsync(CreateApplicationRequest request)
    {
        var app = new ApplicationInfo
        {
            Id = request.AppId,
            DisplayName = request.DisplayName,
            Language = request.Language,
            LanguageVersion = request.LanguageVersion,
            MetadataVersion = "1",
            CodeVersion = string.Empty,
            BlobPath = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _logger.LogInformation("[AppService] Creating application '{AppId}'", request.AppId);
        _store.Applications[app.Id] = app;
        return Task.FromResult(app);
    }

    /// <summary>
    /// Gets an application by ID.
    /// </summary>
    public Task<ApplicationInfo?> GetAsync(string appId)
    {
        _store.Applications.TryGetValue(appId, out var app);
        return Task.FromResult(app);
    }

    /// <summary>
    /// Gets all applications.
    /// </summary>
    public Task<List<ApplicationInfo>> GetAllAsync()
    {
        return Task.FromResult(_store.Applications.Values.ToList());
    }

    /// <summary>
    /// Deploys app code (stores zip in blob storage).
    /// </summary>
    public async Task<DeploymentResponse> DeployAsync(string appId, Stream zipStream, Dictionary<string, string>? environment = null)
    {
        var app = await GetAsync(appId);
        if (app is null)
        {
            throw new InvalidOperationException($"Application '{appId}' not found");
        }

        // Generate version
        var codeVersion = $"v{DateTime.UtcNow:yyyyMMddHHmmss}";
        var blobPath = $"{appId}/{codeVersion}/app.zip";

        _logger.LogInformation("[AppService] Deploying app '{AppId}' version '{Version}' to '{BlobPath}'",
            appId, codeVersion, blobPath);

        // Upload to blob storage
        var blobClient = PackagesContainer.GetBlobClient(blobPath);
        await blobClient.UploadAsync(zipStream, overwrite: true);

        // Update app metadata in memory
        app.CodeVersion = codeVersion;
        app.BlobPath = blobPath;
        app.UpdatedAt = DateTime.UtcNow;
        if (environment is not null)
        {
            foreach (var kv in environment)
            {
                app.Environment[kv.Key] = kv.Value;
            }
        }

        _store.Applications[app.Id] = app;

        return new DeploymentResponse
        {
            AppId = appId,
            CodeVersion = codeVersion,
            BlobPath = blobPath,
            DeployedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets the download URL for an app package.
    /// Returns an absolute blob URL (with SAS token for production, or direct URI for Azurite).
    /// </summary>
    public Task<string> GetDownloadUrlAsync(string appId, string? codeVersion = null)
    {
        var app = _store.Applications.GetValueOrDefault(appId);
        if (app is null)
        {
            throw new InvalidOperationException($"Application '{appId}' not found");
        }

        var version = codeVersion ?? app.CodeVersion;
        if (string.IsNullOrEmpty(version))
        {
            throw new InvalidOperationException($"Application '{appId}' has no deployed code");
        }

        var blobPath = $"{appId}/{version}/app.zip";
        var blobClient = PackagesContainer.GetBlobClient(blobPath);

        // For Azurite (dev emulator), we can generate a SAS URL or use the blob URI directly
        // Generate a SAS token valid for 1 hour (good for the download operation)
        if (blobClient.CanGenerateSasUri)
        {
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = PackagesContainer.Name,
                BlobName = blobPath,
                Resource = "b", // blob
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasUrl = blobClient.GenerateSasUri(sasBuilder).ToString();
            _logger.LogInformation("[AppService] Generated SAS URL for {BlobPath}", blobPath);
            return Task.FromResult(sasUrl);
        }

        // Fallback: return the blob's direct URI (works with Azurite in some configurations)
        _logger.LogInformation("[AppService] Using direct blob URL for {BlobPath}", blobPath);
        return Task.FromResult(blobClient.Uri.ToString());
    }

    /// <summary>
    /// Downloads an app package from blob storage.
    /// </summary>
    public async Task<Stream> DownloadAsync(string appId, string? codeVersion = null)
    {
        var app = _store.Applications.GetValueOrDefault(appId);
        if (app is null)
        {
            throw new InvalidOperationException($"Application '{appId}' not found");
        }

        var version = codeVersion ?? app.CodeVersion;
        if (string.IsNullOrEmpty(version))
        {
            throw new InvalidOperationException($"Application '{appId}' has no deployed code");
        }

        var blobPath = $"{appId}/{version}/app.zip";
        var blobClient = PackagesContainer.GetBlobClient(blobPath);
        
        var response = await blobClient.DownloadStreamingAsync();
        return response.Value.Content;
    }
}
