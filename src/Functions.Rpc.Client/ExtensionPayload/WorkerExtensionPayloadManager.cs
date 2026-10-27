// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Receives and retains one extracted extension source until the root Host and its channels have stopped.
/// </summary>
internal sealed partial class WorkerExtensionPayloadManager(ILogger<WorkerExtensionPayloadManager> logger) : IAsyncDisposable
{
    internal const string CapabilityName = "ExtensionPayload";
    internal const string CapabilityVersion = "1";
    internal const int MaxChunkBytes = 64 * 1024;
    internal const long MaxCompressedBytes = 256L * 1024 * 1024;
    internal const long MaxExpandedBytes = 1024L * 1024 * 1024;
    internal const int MaxManifestBytes = 1024 * 1024;
    internal const int MaxEntries = 10_000;
    internal const int MaxPathDepth = 32;

    private readonly ILogger<WorkerExtensionPayloadManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Lock _stateLock = new();
    private readonly Lock _disposeLock = new();
    private readonly SemaphoreSlim _preparationGate = new(1, 1);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "azure-functions", "extension-payloads", Guid.NewGuid().ToString("N"));
    private string _directoryPath;
    private bool _selected;
    private bool _disposed;
    private Task _disposeTask;

    internal string DirectoryPath
    {
        get
        {
            lock (_stateLock)
            {
                return _directoryPath;
            }
        }
    }

    internal async Task PrepareAsync(RpcClientWorkerChannel channel, CancellationToken cancellationToken)
    {
        using SemaphoreLock gate = await _preparationGate.LockAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_selected)
            {
                return;
            }
        }

        // Only one candidate acquires the root-owned source; later links reuse it without another transfer.
        string capability = channel.ExtensionPayloadCapability;
        if (capability is null)
        {
            SelectSource(null, cancellationToken);
            return;
        }

        if (!string.Equals(capability, CapabilityVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The worker advertised an unsupported extension payload version.");
        }

        string attempt = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(attempt, "content");
        Exception failure = null;
        try
        {
            Directory.CreateDirectory(attempt);
            string zipPath = Path.Combine(attempt, "payload.zip");
            await using (FileStream zip = new(zipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, useAsync: true))
            {
                await channel.ReceiveExtensionPayloadAsync(zip, cancellationToken);
                await zip.FlushAsync(cancellationToken);
            }

            await ExtractAsync(zipPath, staging, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (channel.IsTerminated)
            {
                throw new IOException("The worker disconnected before its extension payload was accepted.");
            }

            SelectSource(staging, cancellationToken);
            Log.PayloadPrepared(_logger, channel.Id);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            if (Directory.Exists(attempt))
            {
                Directory.Delete(attempt, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failure = AggregateException.Combine(failure, exception);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void SelectSource(string staging, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (staging is not null)
            {
                string active = Path.Combine(_root, "active");
                Directory.Move(staging, active);
                _directoryPath = active;
            }

            _selected = true;
        }
    }

    internal static async Task ExtractAsync(
        string zipPath,
        string staging,
        CancellationToken cancellationToken,
        long maxCompressedBytes = MaxCompressedBytes,
        long maxExpandedBytes = MaxExpandedBytes,
        int maxEntries = MaxEntries)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using FileStream zipStream = File.OpenRead(zipPath);
        if (zipStream.Length > maxCompressedBytes)
        {
            throw new InvalidDataException("The compressed extension payload exceeded its size limit.");
        }

        using ZipArchive archive = new(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > maxEntries)
        {
            throw new InvalidDataException("The extension payload exceeded its entry limit.");
        }

        string root = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        HashSet<string> paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Directory.CreateDirectory(root);
        byte[] buffer = new byte[MaxChunkBytes];
        long expandedBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/');
            bool isDirectory = name.EndsWith("/", StringComparison.Ordinal);
            string path = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar))).TrimEnd(Path.DirectorySeparatorChar);
            if (!path.StartsWith(root, comparison) || name.Contains(':', StringComparison.Ordinal) ||
                name.TrimEnd('/').Split('/').Length > MaxPathDepth)
            {
                throw new InvalidDataException("The extension payload contains an unsafe extraction path.");
            }

            // Count implicit parents too; entries are always written as regular files, never restored as links.
            for (string current = path; current.Length >= root.Length; current = Path.GetDirectoryName(current))
            {
                if (paths.Add(current) && paths.Count > maxEntries)
                {
                    throw new InvalidDataException("The extension payload exceeded its filesystem entry limit.");
                }
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            await using Stream input = entry.Open();
            await using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);
            bool isManifest = string.Equals(path, Path.Combine(root, "extensions.json"), comparison);
            long entryBytes = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (read > maxExpandedBytes - expandedBytes)
                {
                    throw new InvalidDataException("The expanded extension payload exceeded its size limit.");
                }

                if (isManifest && read > MaxManifestBytes - entryBytes)
                {
                    throw new InvalidDataException("The extension manifest exceeded its 1 MiB size limit.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                entryBytes += read;
                expandedBytes += read;
            }
        }

        if (!File.Exists(Path.Combine(root, "extensions.json")) || !Directory.Exists(Path.Combine(root, ".azurefunctions")))
        {
            throw new InvalidDataException("The extension payload is missing its manifest or extension directory.");
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            return new(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        lock (_stateLock)
        {
            _disposed = true;
        }

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        finally
        {
            _preparationGate.Dispose();
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Extension payload for worker {WorkerId} was extracted and its source accepted.")]
        public static partial void PayloadPrepared(ILogger logger, string workerId);
    }
}
