// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.ExtensionArtifacts;

/// <summary>
/// Compatibility shim that creates extension artifacts for worker SDKs that do not provide them.
/// </summary>
internal sealed partial class ExtensionArtifactShim(ILogger<ExtensionArtifactShim> logger) : IExtensionArtifactShim
{
    private const string AzureFunctionsDirectoryName = ".azurefunctions";
    private const string ExtensionsJsonFileName = "extensions.json";

    /// <summary>
    /// Permissions stamped on every archive entry. Fixed so that the permissions a worker sees
    /// after extraction do not vary with how the deployment happened to be unpacked.
    /// </summary>
    private const UnixFileMode ArtifactEntryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <summary>
    /// Enumeration options for the extensions directory.
    /// <see cref="EnumerationOptions.AttributesToSkip"/> defaults to <c>Hidden | System</c>,
    /// which would drop the dotfiles this archive is made of, so it is retargeted at reparse
    /// points: a link is not guaranteed to point inside the deployment, and skipping links
    /// also keeps the walk from descending into a linked directory.
    /// <see cref="EnumerationOptions.IgnoreInaccessible"/> is cleared so that an unreadable
    /// file faults the walk instead of quietly shrinking the archive.
    /// </summary>
    private static readonly EnumerationOptions ExtensionFileEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
    };

    /// <summary>
    /// Why an artifact input cannot be used, or <see cref="Usable"/> when it can.
    /// </summary>
    private enum ArtifactPathState
    {
        /// <summary>The path exists and is of the expected kind.</summary>
        Usable,

        /// <summary>Nothing exists at the path.</summary>
        NotFound,

        /// <summary>A file exists where a directory was expected, or the reverse.</summary>
        WrongType,
    }

    /// <inheritdoc />
    public async Task<ExtensionArtifact?> CreateAsync(string functionAppDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionAppDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        string extensionsJsonPath = Path.Combine(functionAppDirectory, ExtensionsJsonFileName);

        ArtifactPathState extensionsJsonState = ProbeArtifactPath(extensionsJsonPath, expectDirectory: false);

        if (extensionsJsonState is not ArtifactPathState.Usable)
        {
            string reason = extensionsJsonState switch
            {
                ArtifactPathState.NotFound => DescribeMissingExtensionsJson(functionAppDirectory, extensionsJsonPath),
                _ => $"'{extensionsJsonPath}' is not a file",
            };

            LogArtifactsUnavailable(logger, reason);

            return null;
        }

        string azureFunctionsDirectory = Path.Combine(functionAppDirectory, AzureFunctionsDirectoryName);
        ArtifactPathState azureFunctionsState = ProbeArtifactPath(azureFunctionsDirectory, expectDirectory: true);

        if (azureFunctionsState is not ArtifactPathState.Usable)
        {
            string reason = azureFunctionsState switch
            {
                ArtifactPathState.NotFound => $"No {AzureFunctionsDirectoryName} directory found at '{azureFunctionsDirectory}'",
                _ => $"'{azureFunctionsDirectory}' is not a directory",
            };

            LogArtifactsUnavailable(logger, reason);

            return null;
        }

        List<(string EntryName, string FilePath)> extensionEntries = CollectExtensionEntries(azureFunctionsDirectory, cancellationToken);

        if (extensionEntries.Count == 0)
        {
            LogArtifactsUnavailable(logger, $"No extension files found under '{azureFunctionsDirectory}'");

            return null;
        }

        byte[] payload = await CreateArchiveAsync(extensionsJsonPath, extensionEntries, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        LogArchivePrepared(logger, extensionEntries.Count + 1, payload.LongLength);

        return new ExtensionArtifact(payload);
    }

    /// <summary>
    /// Reports whether an artifact input can be used, distinguishing an absent path from one
    /// that cannot be inspected. <see cref="File.Exists(string)"/> and
    /// <see cref="Directory.Exists(string)"/> answer <see langword="false"/> for a denied or
    /// failing path just as they do for a missing one, which would report an unreadable
    /// deployment as one that simply carries no extensions. Access and I/O failures propagate
    /// instead, matching the enumeration walk, which faults rather than yielding an archive
    /// that covers less than the deployment.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    /// <param name="expectDirectory">
    /// <see langword="true"/> when a directory is required, <see langword="false"/> when a file
    /// is required.
    /// </param>
    /// <exception cref="UnauthorizedAccessException">The path cannot be inspected.</exception>
    /// <exception cref="IOException">Inspecting the path failed.</exception>
    private static ArtifactPathState ProbeArtifactPath(string path, bool expectDirectory)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return ArtifactPathState.NotFound;
        }

        // Attributes are readable for a directory standing where a file belongs, so the kind is
        // what separates a usable input from one that only fails later, on open or on walk.
        bool isDirectory = (attributes & FileAttributes.Directory) != 0;

        return isDirectory == expectDirectory ? ArtifactPathState.Usable : ArtifactPathState.WrongType;
    }

    /// <summary>
    /// Describes an absent <c>extensions.json</c>. A function app directory that is empty, or
    /// that does not exist, means the deployment never landed, which is a different failure from
    /// a publish output that carries content but not this file, so the two are reported apart.
    /// Only reached once the file is known to be absent, so the walk costs nothing in the
    /// ordinary case.
    /// </summary>
    private static string DescribeMissingExtensionsJson(string functionAppDirectory, string extensionsJsonPath)
    {
        bool hasContent;
        try
        {
            using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(functionAppDirectory).GetEnumerator();
            hasContent = entries.MoveNext();
        }
        catch (DirectoryNotFoundException)
        {
            return $"Function app directory '{functionAppDirectory}' does not exist";
        }

        return hasContent
            ? $"No {ExtensionsJsonFileName} found at '{extensionsJsonPath}'"
            : $"Function app directory '{functionAppDirectory}' is empty";
    }

    /// <summary>
    /// Collects an archive entry for every file under the extensions directory, ordered by
    /// entry name. Symbolic links, and the contents of symbolically linked directories, are
    /// excluded so that the archive covers only files that belong to the deployment.
    /// </summary>
    private static List<(string EntryName, string FilePath)> CollectExtensionEntries(
        string azureFunctionsDirectory,
        CancellationToken cancellationToken)
    {
        List<(string EntryName, string FilePath)> extensionEntries = [];

        foreach (string filePath in Directory.EnumerateFiles(azureFunctionsDirectory, "*", ExtensionFileEnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(azureFunctionsDirectory, filePath).Replace(Path.DirectorySeparatorChar, '/');
            extensionEntries.Add(($"{AzureFunctionsDirectoryName}/{relativePath}", filePath));
        }

        // Order by the emitted entry name rather than the source path. Directory enumeration
        // order is a filesystem property on Linux and differs between overlayfs, ext4, tmpfs
        // and file shares, so it cannot produce a reproducible archive on its own.
        extensionEntries.Sort(static (left, right) => string.CompareOrdinal(left.EntryName, right.EntryName));

        return extensionEntries;
    }

    private static async Task<byte[]> CreateArchiveAsync(
        string extensionsJsonPath,
        List<(string EntryName, string FilePath)> extensionEntries,
        CancellationToken cancellationToken)
    {
        using MemoryStream archiveStream = new();

        await using (TarWriter tarWriter = new(archiveStream, leaveOpen: true))
        {
            await WriteEntryAsync(tarWriter, ExtensionsJsonFileName, extensionsJsonPath, cancellationToken);

            foreach ((string entryName, string filePath) in extensionEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteEntryAsync(tarWriter, entryName, filePath, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return archiveStream.ToArray();
    }

    /// <summary>
    /// Writes a single file entry using fixed metadata.
    /// </summary>
    /// <remarks>
    /// Writing a path directly would copy the file's last write time and, on Unix, its
    /// permissions and owner into the entry header, so the archive a worker receives would vary
    /// with how and by whom the deployment was unpacked.
    /// </remarks>
    private static async Task WriteEntryAsync(TarWriter tarWriter, string entryName, string filePath, CancellationToken cancellationToken)
    {
        await using FileStream content = File.OpenRead(filePath);

        PaxTarEntry entry = new(TarEntryType.RegularFile, entryName)
        {
            ModificationTime = DateTimeOffset.UnixEpoch,
            Mode = ArtifactEntryMode,
            DataStream = content,
        };

        await tarWriter.WriteEntryAsync(entry, cancellationToken);
    }
}
