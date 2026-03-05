using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace WorkerModel.RuntimeSidecar.Services;

/// <summary>
/// Mounts app packages using SquashFS (Linux) or falls back to zip extraction (Windows/dev).
/// 
/// In production (Linux containers), uses squashfuse for read-only FUSE mounts.
/// For local development (Windows), extracts the zip to the mount point instead.
/// </summary>
public class SquashFsMounter
{
    private readonly ILogger<SquashFsMounter> _logger;

    public SquashFsMounter(ILogger<SquashFsMounter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Mounts a zip/squashfs package at the specified mount point.
    /// On Linux: uses squashfuse for a true read-only FUSE mount.
    /// On Windows: extracts the zip to the mount point (dev fallback).
    /// </summary>
    public async Task MountAsync(string packagePath, string mountPoint, CancellationToken cancellationToken)
    {
        // Ensure mount point directory exists
        Directory.CreateDirectory(mountPoint);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await MountWithSquashFuseAsync(packagePath, mountPoint, cancellationToken);
        }
        else
        {
            // Windows/macOS dev fallback: extract zip
            await ExtractZipAsync(packagePath, mountPoint, cancellationToken);
        }
    }

    /// <summary>
    /// Unmounts a previously mounted package.
    /// On Linux: uses fusermount -u.
    /// On Windows: deletes extracted files.
    /// </summary>
    public async Task UnmountAsync(string mountPoint, CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await UnmountFuseAsync(mountPoint, cancellationToken);
        }
        else
        {
            // Windows dev fallback: just delete the extracted files
            if (Directory.Exists(mountPoint))
            {
                _logger.LogInformation("[SquashFsMounter] Cleaning up extracted files at {MountPoint}", mountPoint);
                Directory.Delete(mountPoint, recursive: true);
            }
        }
    }

    private async Task MountWithSquashFuseAsync(string packagePath, string mountPoint, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[SquashFsMounter] Mounting {Package} at {MountPoint} via squashfuse",
            packagePath,
            mountPoint);

        // For a zip file, use fuse-zip; for .squashfs, use squashfuse
        var isSquashFs = packagePath.EndsWith(".squashfs", StringComparison.OrdinalIgnoreCase);
        var tool = isSquashFs ? "squashfuse" : "fuse-zip";
        var args = isSquashFs
            ? $"{packagePath} {mountPoint}"
            : $"-r {packagePath} {mountPoint}"; // -r for read-only

        var psi = new ProcessStartInfo
        {
            FileName = tool,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {tool}");

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException(
                $"{tool} failed with exit code {process.ExitCode}: {stderr}");
        }

        _logger.LogInformation("[SquashFsMounter] FUSE mount successful");
    }

    private async Task ExtractZipAsync(string packagePath, string mountPoint, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[SquashFsMounter] Extracting {Package} to {MountPoint} (dev fallback)",
            packagePath,
            mountPoint);

        await Task.Run(() =>
        {
            ZipFile.ExtractToDirectory(packagePath, mountPoint, overwriteFiles: true);
        }, cancellationToken);

        var fileCount = Directory.GetFiles(mountPoint, "*", SearchOption.AllDirectories).Length;
        _logger.LogInformation("[SquashFsMounter] Extracted {FileCount} files to {MountPoint}", fileCount, mountPoint);
    }

    private async Task UnmountFuseAsync(string mountPoint, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[SquashFsMounter] Unmounting FUSE at {MountPoint}", mountPoint);

        var psi = new ProcessStartInfo
        {
            FileName = "fusermount",
            Arguments = $"-u {mountPoint}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start fusermount");

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            _logger.LogWarning("[SquashFsMounter] fusermount failed: {Error}", stderr);
        }
        else
        {
            _logger.LogInformation("[SquashFsMounter] FUSE unmount successful");
        }
    }
}
