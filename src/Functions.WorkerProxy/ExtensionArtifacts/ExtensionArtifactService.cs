// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Formats.Tar;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Artifacts;

/// <summary>
/// Archives extension artifacts (extensions.json + .azurefunctions/) at startup and serves
/// them via an HTTP endpoint. Initialization starts immediately on construction and the
/// endpoint awaits completion.
/// </summary>
internal sealed partial class ExtensionArtifactService : IDisposable
{
    private const string ExtensionsJsonFileName = "extensions.json";
    private const string AzureFunctionsFolderName = ".azurefunctions";

    private readonly Task _initTask;
    private readonly ILogger<ExtensionArtifactService> _logger;
    private string? _archivePath;
    private string? _digest;

    public ExtensionArtifactService(
        IOptions<WorkerProxyOptions> options,
        ILogger<ExtensionArtifactService> logger)
    {
        _logger = logger;
        _initTask = Task.Run(() => InitializeAsync(options.Value, CancellationToken.None));
    }

    private bool IsAvailable => _archivePath is not null;

    private string? Digest => _digest;

    private string? ArchivePath => _archivePath;

    private Task WaitForReadyAsync() => _initTask;

    private async Task InitializeAsync(
        WorkerProxyOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.AppContentRoot))
        {
            LogArtifactServingDisabled(_logger, "APP_CONTENT_ROOT is not configured");
            return;
        }

        string extensionsJsonPath = Path.Combine(options.AppContentRoot, ExtensionsJsonFileName);
        string azureFunctionsDir = Path.Combine(options.AppContentRoot, AzureFunctionsFolderName);

        if (!File.Exists(extensionsJsonPath))
        {
            LogArtifactServingDisabled(_logger, $"No extensions.json found at '{extensionsJsonPath}'");
            return;
        }

        if (!Directory.Exists(azureFunctionsDir))
        {
            LogArtifactServingDisabled(_logger, $"No .azurefunctions directory found at '{azureFunctionsDir}'");
            return;
        }

        string archivePath = Path.Combine(Path.GetTempPath(), $"extension-artifacts-{Guid.NewGuid():N}.tar");
        string digest = await CreateArchiveAsync(extensionsJsonPath, azureFunctionsDir, archivePath, cancellationToken);

        _archivePath = archivePath;
        _digest = digest;

        LogArchivePrepared(_logger, archivePath, digest, new FileInfo(archivePath).Length);
    }

    private static async Task<string> CreateArchiveAsync(
        string extensionsJsonPath,
        string azureFunctionsDir,
        string outputPath,
        CancellationToken cancellationToken)
    {
        await using FileStream outputStream = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
        await using TarWriter tarWriter = new(outputStream, leaveOpen: true);

        // Add extensions.json at the root of the archive.
        await tarWriter.WriteEntryAsync(extensionsJsonPath, ExtensionsJsonFileName, cancellationToken);

        // Add all files under .azurefunctions/.
        foreach (string filePath in Directory.EnumerateFiles(azureFunctionsDir, "*", SearchOption.AllDirectories))
        {
            string entryName = Path.Combine(AzureFunctionsFolderName, Path.GetRelativePath(azureFunctionsDir, filePath));
            entryName = entryName.Replace('\\', '/');
            await tarWriter.WriteEntryAsync(filePath, entryName, cancellationToken);
        }

        await outputStream.FlushAsync(cancellationToken);

        // Compute SHA-256 digest of the archive.
        outputStream.Position = 0;
        byte[] hash = await SHA256.HashDataAsync(outputStream, cancellationToken);

        return $"sha256:{Convert.ToHexStringLower(hash)}";
    }

    /// <summary>
    /// Minimal API endpoint handler for GET /admin/artifacts/extensions.
    /// </summary>
    internal static async Task<IResult> HandleRequest(HttpContext context, ExtensionArtifactService artifactService)
    {
        await artifactService.WaitForReadyAsync();

        if (!artifactService.IsAvailable)
        {
            return TypedResults.NotFound("Extension artifacts are not available.");
        }

        string digest = artifactService.Digest!;
        string? ifNoneMatch = context.Request.Headers.IfNoneMatch;

        if (string.Equals(ifNoneMatch, $"\"{digest}\"", StringComparison.Ordinal)
            || string.Equals(ifNoneMatch, digest, StringComparison.Ordinal))
        {
            context.Response.Headers.ETag = $"\"{digest}\"";
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        context.Response.Headers.ETag = $"\"{digest}\"";
        return TypedResults.PhysicalFile(artifactService.ArchivePath!, "application/x-tar");
    }

    public void Dispose()
    {
        if (_archivePath is not null && File.Exists(_archivePath))
        {
            try
            {
                File.Delete(_archivePath);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
