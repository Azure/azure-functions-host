namespace WorkerModel.RuntimeSidecar.Services;

/// <summary>
/// Downloads app packages (zip files) from blob storage or SC download endpoint
/// and caches them locally for mounting.
/// </summary>
public class PackageDownloader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PackageDownloader> _logger;

    public PackageDownloader(IHttpClientFactory httpClientFactory, ILogger<PackageDownloader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Downloads a package from the given URL to the local cache directory.
    /// Returns the local file path of the downloaded package.
    /// </summary>
    /// <param name="packageUrl">URL to download from (blob SAS URL or SC endpoint).</param>
    /// <param name="cachePath">Local cache directory.</param>
    /// <param name="applicationId">App identifier (used in cache filename).</param>
    /// <param name="codeVersion">Code version (used in cache filename).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the locally cached package file.</returns>
    public async Task<string> DownloadAsync(
        string packageUrl,
        string cachePath,
        string applicationId,
        string codeVersion,
        CancellationToken cancellationToken)
    {
        // Ensure cache directory exists
        Directory.CreateDirectory(cachePath);

        var fileName = $"{applicationId}-{codeVersion}.zip";
        var localPath = Path.Combine(cachePath, fileName);

        // Skip download if already cached
        if (File.Exists(localPath))
        {
            _logger.LogInformation("[PackageDownloader] Using cached package: {Path}", localPath);
            return localPath;
        }

        _logger.LogInformation(
            "[PackageDownloader] Downloading from {Url} to {Path}...",
            packageUrl,
            localPath);

        var client = _httpClientFactory.CreateClient();

        using var response = await client.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        // Write to a temp file first, then rename (atomic-ish on same volume)
        var tempPath = localPath + ".tmp";
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920);
            
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    var pct = (double)totalRead / totalBytes.Value * 100;
                    if (totalRead % (1024 * 1024) < bytesRead) // Log every ~1MB
                    {
                        _logger.LogDebug("[PackageDownloader] Progress: {Pct:F1}%", pct);
                    }
                }
            }

            _logger.LogInformation("[PackageDownloader] Downloaded {Bytes} bytes", totalRead);
        }
        catch
        {
            // Clean up temp file on failure
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }

        // Move temp to final path
        File.Move(tempPath, localPath, overwrite: true);

        return localPath;
    }
}
