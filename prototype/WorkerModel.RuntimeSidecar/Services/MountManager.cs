using System.Runtime.InteropServices;

namespace WorkerModel.RuntimeSidecar.Services;

/// <summary>
/// Manages the lifecycle of app package mounts.
/// Coordinates download → mount → expose for the Runtime pod.
/// </summary>
public class MountManager
{
    private readonly PackageDownloader _downloader;
    private readonly SquashFsMounter _mounter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MountManager> _logger;
    private readonly object _lock = new();
    private MountInfo? _currentMount;

    public MountManager(
        PackageDownloader downloader,
        SquashFsMounter mounter,
        IConfiguration configuration,
        ILogger<MountManager> logger)
    {
        _downloader = downloader;
        _mounter = mounter;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current mount info, or null if nothing is mounted.
    /// </summary>
    public MountInfo? GetMountInfo()
    {
        lock (_lock)
        {
            return _currentMount;
        }
    }

    /// <summary>
    /// Downloads the app package and mounts it at the target mount point.
    /// </summary>
    public async Task<MountInfo> MountAsync(Controllers.MountRequest request, CancellationToken cancellationToken)
    {
        var cachePath = _configuration["RuntimeSidecar:CachePath"] ?? GetDefaultCachePath();
        var mountPoint = request.MountPoint ?? GetDefaultMountPoint(request.ApplicationId);

        var mountInfo = new MountInfo
        {
            ApplicationId = request.ApplicationId,
            CodeVersion = request.CodeVersion,
            MountPoint = mountPoint,
            State = MountState.Downloading,
        };

        lock (_lock)
        {
            _currentMount = mountInfo;
        }

        try
        {
            // Step 1: Download the zip package to local cache
            _logger.LogInformation(
                "[MountManager] Downloading package for {AppId} v{Version}...",
                request.ApplicationId,
                request.CodeVersion);

            var cachedPath = await _downloader.DownloadAsync(
                request.PackageUrl,
                cachePath,
                request.ApplicationId,
                request.CodeVersion,
                cancellationToken);

            mountInfo.CachedPackagePath = cachedPath;
            mountInfo.State = MountState.Mounting;

            // Step 2: Mount the package at the target mount point
            _logger.LogInformation(
                "[MountManager] Mounting {CachedPath} at {MountPoint}...",
                cachedPath,
                mountPoint);

            await _mounter.MountAsync(cachedPath, mountPoint, cancellationToken);

            mountInfo.State = MountState.Ready;
            mountInfo.MountedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "[MountManager] Mount complete: {AppId} v{Version} at {MountPoint}",
                request.ApplicationId,
                request.CodeVersion,
                mountPoint);

            return mountInfo;
        }
        catch (Exception ex)
        {
            mountInfo.State = MountState.Failed;
            mountInfo.Error = ex.Message;
            _logger.LogError(ex, "[MountManager] Mount failed for {AppId}", request.ApplicationId);
            throw;
        }
    }

    /// <summary>
    /// Unmounts the current package and cleans up cached files.
    /// </summary>
    public async Task UnmountAsync(CancellationToken cancellationToken)
    {
        MountInfo? mount;
        lock (_lock)
        {
            mount = _currentMount;
        }

        if (mount is null)
        {
            return;
        }

        _logger.LogInformation("[MountManager] Unmounting {MountPoint}...", mount.MountPoint);

        if (mount.State is MountState.Ready)
        {
            await _mounter.UnmountAsync(mount.MountPoint, cancellationToken);
        }

        // Clean up cached package
        if (mount.CachedPackagePath is not null && File.Exists(mount.CachedPackagePath))
        {
            File.Delete(mount.CachedPackagePath);
        }

        lock (_lock)
        {
            _currentMount = null;
        }

        _logger.LogInformation("[MountManager] Unmount complete");
    }

    /// <summary>
    /// Gets the default cache path for downloaded packages.
    /// On Windows: %TEMP%\functions-cache
    /// On Linux: /var/cache/functions
    /// </summary>
    private static string GetDefaultCachePath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(Path.GetTempPath(), "functions-cache");
        }
        return "/var/cache/functions";
    }

    /// <summary>
    /// Gets the default mount point for an app.
    /// On Windows: %TEMP%\functions-apps\{appId} (unique per app)
    /// On Linux: /home/site/wwwroot
    /// </summary>
    private static string GetDefaultMountPoint(string appId)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Create unique temp directory per app on Windows
            var appsRoot = Path.Combine(Path.GetTempPath(), "functions-apps");
            return Path.Combine(appsRoot, appId);
        }
        return "/home/site/wwwroot";
    }
}

/// <summary>
/// Information about a mounted app package.
/// </summary>
public class MountInfo
{
    public string ApplicationId { get; set; } = string.Empty;
    public string CodeVersion { get; set; } = string.Empty;
    public string MountPoint { get; set; } = string.Empty;
    public string? CachedPackagePath { get; set; }
    public MountState State { get; set; }
    public DateTimeOffset? MountedAt { get; set; }
    public string? Error { get; set; }

    public bool IsReady => State is MountState.Ready;
}

/// <summary>
/// The lifecycle state of a mount operation.
/// </summary>
public enum MountState
{
    Downloading,
    Mounting,
    Ready,
    Failed,
}
