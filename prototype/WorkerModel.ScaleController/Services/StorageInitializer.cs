using Azure.Storage.Blobs;

namespace WorkerModel.ScaleController.Services;

/// <summary>
/// Initializes blob storage container on startup.
/// Metadata is in-memory (no Cosmos), packages are in Azure Storage.
/// </summary>
public class StorageInitializer : IHostedService
{
    private const int MaxRetries = 30;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(15);

    private readonly BlobServiceClient _blobClient;
    private readonly ILogger<StorageInitializer> _logger;
    private readonly IConfiguration _config;

    public StorageInitializer(
        BlobServiceClient blobClient,
        ILogger<StorageInitializer> logger,
        IConfiguration config)
    {
        _blobClient = blobClient;
        _logger = logger;
        _config = config;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[StorageInit] Initializing storage (in-memory metadata, Azure blobs for packages)...");

        // Initialize Blob Storage (with retries for emulator startup)
        await InitializeWithRetryAsync("Blob", InitializeBlobAsync, cancellationToken);

        _logger.LogInformation("[StorageInit] Storage initialization complete");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task InitializeWithRetryAsync(string name, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(PerAttemptTimeout);
                await action(cts.Token);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                var msg = ex is OperationCanceledException ? "Timed out" : ex.Message;
                _logger.LogWarning("[StorageInit] {Name} initialization attempt {Attempt}/{Max} failed: {Message}. Retrying in {Delay}s...",
                    name, attempt, MaxRetries, msg, RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    private async Task InitializeBlobAsync(CancellationToken cancellationToken)
    {
        var containerName = _config["ScaleController:BlobContainer"] ?? "function-apps";

        _logger.LogInformation("[StorageInit] Creating blob container '{Container}'...", containerName);
        var containerClient = _blobClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }
}
