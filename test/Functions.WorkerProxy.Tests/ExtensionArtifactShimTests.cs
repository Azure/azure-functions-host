// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.ExtensionArtifacts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class ExtensionArtifactShimTests
{
    /// <summary>
    /// Permissions every archive entry is expected to carry: owner read and write, group and
    /// other read. Restated here rather than shared with the shim so that relaxing the
    /// production value has to be a deliberate edit in both places.
    /// </summary>
    private const UnixFileMode ExpectedEntryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    [Fact]
    public async Task CreateAsync_ReturnsOrderedArchiveWithCompletePayload()
    {
        using TestDirectory contentRoot = new();

        (byte[] ExtensionsJson, byte[] FirstAssembly, byte[] NestedAssembly, byte[] LastAssembly) expectedFiles =
            await CreateFunctionAppLayoutAsync(contentRoot.Path);

        // Stamp metadata the archive must not inherit. Without this the asserted mode would
        // match whatever the umask produced and prove nothing.
        StampMetadata(
            contentRoot.Path,
            new DateTime(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        ExtensionArtifactShim shim = CreateShim();

        ExtensionArtifact artifact = Assert.IsType<ExtensionArtifact>(await shim.CreateAsync(contentRoot.Path, CancellationToken.None));

        byte[] payload = artifact.Payload.ToArray();
        // Verify root manifest placement, preserved .azurefunctions relative paths, and
        // ordinal archive entry ordering.
        IReadOnlyList<ArchiveEntry> entries = ReadArchive(payload);
        Assert.Collection(
            entries,
            entry => AssertArchiveEntry(entry, "extensions.json", expectedFiles.ExtensionsJson),
            entry => AssertArchiveEntry(entry, ".azurefunctions/a-extension.dll", expectedFiles.FirstAssembly),
            entry => AssertArchiveEntry(entry, ".azurefunctions/nested/m-extension.dll", expectedFiles.NestedAssembly),
            entry => AssertArchiveEntry(entry, ".azurefunctions/zz-extension.dll", expectedFiles.LastAssembly));
    }

    [Fact]
    public async Task CreateAsync_IdenticalContentProducesIdenticalPayload()
    {
        using TestDirectory firstRoot = new();
        using TestDirectory secondRoot = new();
        await CreateFunctionAppLayoutAsync(firstRoot.Path);
        await CreateFunctionAppLayoutAsync(secondRoot.Path);

        // Same bytes, but the filesystem metadata a deployment happens to carry differs.
        const UnixFileMode RestrictiveMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        const UnixFileMode PermissiveMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        StampMetadata(firstRoot.Path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), RestrictiveMode);
        StampMetadata(secondRoot.Path, new DateTime(2024, 7, 7, 12, 30, 0, DateTimeKind.Utc), PermissiveMode);

        ExtensionArtifactShim shim = CreateShim();

        ExtensionArtifact first = Assert.IsType<ExtensionArtifact>(await shim.CreateAsync(firstRoot.Path, CancellationToken.None));
        ExtensionArtifact second = Assert.IsType<ExtensionArtifact>(await shim.CreateAsync(secondRoot.Path, CancellationToken.None));

        Assert.Equal(first.Payload.ToArray(), second.Payload.ToArray());
    }

    [Fact]
    public async Task CreateAsync_DifferingContentProducesDifferingPayload()
    {
        using TestDirectory firstRoot = new();
        using TestDirectory secondRoot = new();
        await CreateFunctionAppLayoutAsync(firstRoot.Path);
        await CreateFunctionAppLayoutAsync(secondRoot.Path);
        await File.WriteAllBytesAsync(Path.Combine(secondRoot.Path, ".azurefunctions", "a-extension.dll"), [9, 9, 9, 9]);

        ExtensionArtifactShim shim = CreateShim();

        ExtensionArtifact first = Assert.IsType<ExtensionArtifact>(await shim.CreateAsync(firstRoot.Path, CancellationToken.None));
        ExtensionArtifact second = Assert.IsType<ExtensionArtifact>(await shim.CreateAsync(secondRoot.Path, CancellationToken.None));

        Assert.NotEqual(first.Payload.ToArray(), second.Payload.ToArray());
    }

    [Fact]
    public async Task CreateAsync_NullFunctionAppDirectoryThrows()
    {
        ExtensionArtifactShim shim = CreateShim();

        await Assert.ThrowsAsync<ArgumentNullException>(() => shim.CreateAsync(null!, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CreateAsync_EmptyOrWhitespaceFunctionAppDirectoryThrows(string functionAppDirectory)
    {
        ExtensionArtifactShim shim = CreateShim();

        await Assert.ThrowsAsync<ArgumentException>(() => shim.CreateAsync(functionAppDirectory, CancellationToken.None));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CreateAsync_ReturnsNullWhenRequiredInputIsUnavailable(bool createExtensionsJson, bool createAzureFunctionsDirectory)
    {
        using TestDirectory contentRoot = new();

        if (createExtensionsJson)
        {
            await File.WriteAllTextAsync(Path.Combine(contentRoot.Path, "extensions.json"), "{}");
        }

        if (createAzureFunctionsDirectory)
        {
            Directory.CreateDirectory(Path.Combine(contentRoot.Path, ".azurefunctions"));
        }

        ExtensionArtifactShim shim = CreateShim();

        ExtensionArtifact? artifact = await shim.CreateAsync(contentRoot.Path, CancellationToken.None);

        Assert.Null(artifact);
    }

    [Fact]
    public async Task CreateAsync_ReturnsNullWhenAzureFunctionsDirectoryHoldsNoFiles()
    {
        using TestDirectory contentRoot = new();
        await File.WriteAllTextAsync(Path.Combine(contentRoot.Path, "extensions.json"), "{}");
        // A directory carrying no extension assemblies is the same logical state as no
        // directory at all, so an empty subdirectory must not make it look otherwise.
        Directory.CreateDirectory(Path.Combine(contentRoot.Path, ".azurefunctions", "empty-nested"));

        RecordingLogger<ExtensionArtifactShim> logger = new();
        ExtensionArtifactShim shim = CreateShim(logger);

        ExtensionArtifact? artifact = await shim.CreateAsync(contentRoot.Path, CancellationToken.None);

        Assert.Null(artifact);
        (LogLevel Level, string Message) entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task CreateAsync_ReportsMissingInputsAsWarning(bool createExtensionsJson, bool createAzureFunctionsDirectory)
    {
        using TestDirectory contentRoot = new();

        if (createExtensionsJson)
        {
            await File.WriteAllTextAsync(Path.Combine(contentRoot.Path, "extensions.json"), "{}");
        }

        if (createAzureFunctionsDirectory)
        {
            Directory.CreateDirectory(Path.Combine(contentRoot.Path, ".azurefunctions"));
        }

        RecordingLogger<ExtensionArtifactShim> logger = new();
        ExtensionArtifactShim shim = CreateShim(logger);

        Assert.Null(await shim.CreateAsync(contentRoot.Path, CancellationToken.None));

        // Both inputs are part of every publish output, so an absent one points at a deployment
        // package that was assembled incorrectly. That has to survive a production log level.
        (LogLevel Level, string Message) entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    [Fact]
    public async Task CreateAsync_ReportsEmptyFunctionAppDirectoryAsWarning()
    {
        using TestDirectory contentRoot = new();

        RecordingLogger<ExtensionArtifactShim> logger = new();
        ExtensionArtifactShim shim = CreateShim(logger);

        Assert.Null(await shim.CreateAsync(contentRoot.Path, CancellationToken.None));

        // An app directory holding nothing means the deployment never landed, which points
        // somewhere different than a publish output that is only missing extensions.json.
        (LogLevel Level, string Message) entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("is empty", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_ReportsMissingFunctionAppDirectoryAsWarning()
    {
        using TestDirectory contentRoot = new();
        string missingDirectory = Path.Combine(contentRoot.Path, "never-deployed");

        RecordingLogger<ExtensionArtifactShim> logger = new();
        ExtensionArtifactShim shim = CreateShim(logger);

        Assert.Null(await shim.CreateAsync(missingDirectory, CancellationToken.None));

        (LogLevel Level, string Message) entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("does not exist", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_ReportsMissingExtensionsJsonWhenDirectoryHasOtherContent()
    {
        using TestDirectory contentRoot = new();
        Directory.CreateDirectory(Path.Combine(contentRoot.Path, ".azurefunctions"));

        RecordingLogger<ExtensionArtifactShim> logger = new();
        ExtensionArtifactShim shim = CreateShim(logger);

        Assert.Null(await shim.CreateAsync(contentRoot.Path, CancellationToken.None));

        // A directory whose only entry is the dotfile directory still counts as deployed, so
        // this must report the absent file rather than an empty directory.
        (LogLevel Level, string Message) entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("No extensions.json found", entry.Message, StringComparison.Ordinal);
    }

    [RequiresSymbolicLinkSupportFact]
    public async Task CreateAsync_ExcludesSymbolicLinkedContent()
    {
        using TestDirectory linkedRoot = new();
        using TestDirectory cleanRoot = new();
        using TestDirectory outsideRoot = new();
        await CreateFunctionAppLayoutAsync(linkedRoot.Path);
        await CreateFunctionAppLayoutAsync(cleanRoot.Path);

        // Content the deployment does not own: a file a link can point at, and a directory
        // whose file only enters the archive if enumeration follows a linked directory.
        string outsideFile = Path.Combine(outsideRoot.Path, "outside-extension.dll");
        await File.WriteAllBytesAsync(outsideFile, [7, 7, 7, 7]);
        string outsideDirectory = Path.Combine(outsideRoot.Path, "outside-directory");
        Directory.CreateDirectory(outsideDirectory);
        await File.WriteAllBytesAsync(Path.Combine(outsideDirectory, "leaked-extension.dll"), [8, 8, 8, 8]);
        CreateSymbolicLinks(Path.Combine(linkedRoot.Path, ".azurefunctions"), outsideFile, outsideDirectory);

        ExtensionArtifactShim shim = CreateShim();

        ExtensionArtifact linked = Assert.IsType<ExtensionArtifact>(await shim.CreateAsync(linkedRoot.Path, CancellationToken.None));
        ExtensionArtifact clean = Assert.IsType<ExtensionArtifact>(await shim.CreateAsync(cleanRoot.Path, CancellationToken.None));

        // A link's target is not guaranteed to belong to the deployment, so neither the link
        // itself nor the content behind a linked directory may reach the archive. A dangling
        // link must be skipped rather than fail the read.
        IEnumerable<string> entryNames = ReadArchive(linked.Payload.ToArray())
            .Select(static entry => entry.Name);
        Assert.DoesNotContain(
            entryNames,
            static name => name.Contains("link", StringComparison.Ordinal)
                || name.Contains("leaked", StringComparison.Ordinal));
        // Identical to a layout that never held links, so linked content cannot move the
        // archive either.
        Assert.Equal(clean.Payload.ToArray(), linked.Payload.ToArray());
    }

    [Fact]
    public async Task CreateAsync_ArchivesHiddenExtensionFiles()
    {
        using TestDirectory contentRoot = new();
        await CreateFunctionAppLayoutAsync(contentRoot.Path);
        // Every dot-prefixed file is hidden on Unix. Filtering hidden entries would drop them
        // from the archive silently, leaving an archive that covers less than the deployment.
        await File.WriteAllBytesAsync(Path.Combine(contentRoot.Path, ".azurefunctions", ".hidden-extension.dll"), [3, 3, 3, 3]);

        ExtensionArtifactShim shim = CreateShim();

        ExtensionArtifact artifact = Assert.IsType<ExtensionArtifact>(await shim.CreateAsync(contentRoot.Path, CancellationToken.None));

        Assert.Contains(
            ReadArchive(artifact.Payload.ToArray()),
            entry => string.Equals(entry.Name, ".azurefunctions/.hidden-extension.dll", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_CanceledTokenCancelsArtifactCreation()
    {
        using TestDirectory contentRoot = new();
        await File.WriteAllTextAsync(Path.Combine(contentRoot.Path, "extensions.json"), "{}");
        Directory.CreateDirectory(Path.Combine(contentRoot.Path, ".azurefunctions"));
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();
        ExtensionArtifactShim shim = CreateShim();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shim.CreateAsync(contentRoot.Path, cancellationSource.Token));
    }

    [Fact]
    public async Task CreateAsync_ReportsUnavailableWhenExtensionsJsonIsADirectory()
    {
        using TestDirectory contentRoot = new();
        Directory.CreateDirectory(Path.Combine(contentRoot.Path, "extensions.json"));
        Directory.CreateDirectory(Path.Combine(contentRoot.Path, ".azurefunctions"));
        RecordingLogger<ExtensionArtifactShim> logger = new();
        ExtensionArtifactShim shim = CreateShim(logger);

        Assert.Null(await shim.CreateAsync(contentRoot.Path, CancellationToken.None));

        // Attributes read back fine for a directory, so only the kind check rejects it here.
        (LogLevel Level, string Message) entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("is not a file", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_ReportsUnavailableWhenAzureFunctionsIsAFile()
    {
        using TestDirectory contentRoot = new();
        await File.WriteAllTextAsync(Path.Combine(contentRoot.Path, "extensions.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(contentRoot.Path, ".azurefunctions"), "not a directory");
        RecordingLogger<ExtensionArtifactShim> logger = new();
        ExtensionArtifactShim shim = CreateShim(logger);

        Assert.Null(await shim.CreateAsync(contentRoot.Path, CancellationToken.None));

        (LogLevel Level, string Message) entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("is not a directory", entry.Message, StringComparison.Ordinal);
    }

    [RequiresPermissionEnforcementFact]
    public async Task CreateAsync_PropagatesAccessFailureInsteadOfReportingMissingInputs()
    {
        using TestDirectory contentRoot = new();
        string appDirectory = Path.Combine(contentRoot.Path, "app");
        Directory.CreateDirectory(appDirectory);
        await File.WriteAllTextAsync(Path.Combine(appDirectory, "extensions.json"), "{}");
        Directory.CreateDirectory(Path.Combine(appDirectory, ".azurefunctions"));
        DenyDirectoryAccess(appDirectory);

        try
        {
            ExtensionArtifactShim shim = CreateShim();

            // The inputs are present; only permission hides them. Reporting that as an app
            // carrying no extensions would start a worker that silently lacks them.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shim.CreateAsync(appDirectory, CancellationToken.None));
        }
        finally
        {
            RestoreDirectoryAccess(appDirectory);
        }
    }

    /// <summary>
    /// Removes every permission from a directory. The caller is responsible for restoring
    /// access, and for carrying <see cref="RequiresPermissionEnforcementFactAttribute"/> so the
    /// denial is known to bind.
    /// </summary>
    private static void DenyDirectoryAccess(string directory)
    {
        // Unreachable on Windows, because callers carry
        // RequiresPermissionEnforcementFactAttribute and are skipped there. The guard is what
        // lets the platform analyzer see that.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.None);
        }
    }

    private static void RestoreDirectoryAccess(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static ExtensionArtifactShim CreateShim()
    {
        return new ExtensionArtifactShim(NullLogger<ExtensionArtifactShim>.Instance);
    }

    private static ExtensionArtifactShim CreateShim(ILogger<ExtensionArtifactShim> logger)
    {
        return new ExtensionArtifactShim(logger);
    }

    /// <summary>Creates the temporary function-app layout used by the archive test.</summary>
    private static async Task<(
        byte[] ExtensionsJson,
        byte[] FirstAssembly,
        byte[] NestedAssembly,
        byte[] LastAssembly)> CreateFunctionAppLayoutAsync(string contentRoot)
    {
        byte[] extensionsJson = Encoding.UTF8.GetBytes("""{"extensions":[]}""");
        byte[] firstAssembly = [1, 2, 3, 4];
        byte[] nestedAssembly = [5, 6, 7, 8];
        byte[] lastAssembly = [9, 10, 11, 12];
        await File.WriteAllBytesAsync(Path.Combine(contentRoot, "extensions.json"), extensionsJson);
        string azureFunctionsDirectory = Path.Combine(contentRoot, ".azurefunctions");
        string nestedDirectory = Path.Combine(azureFunctionsDirectory, "nested");
        Directory.CreateDirectory(nestedDirectory);
        // zz-extension.dll sits beside the nested directory but sorts after everything inside
        // it. Enumeration always yields a directory's own files before recursing, so this
        // layout can only produce the asserted order if the entries are explicitly sorted.
        await File.WriteAllBytesAsync(Path.Combine(nestedDirectory, "m-extension.dll"), nestedAssembly);
        await File.WriteAllBytesAsync(Path.Combine(azureFunctionsDirectory, "a-extension.dll"), firstAssembly);
        await File.WriteAllBytesAsync(Path.Combine(azureFunctionsDirectory, "zz-extension.dll"), lastAssembly);

        return (extensionsJson, firstAssembly, nestedAssembly, lastAssembly);
    }

    /// <summary>Creates a single symbolic link.</summary>
    private static void CreateSymbolicLink(string path, string target, bool isDirectory)
    {
        if (isDirectory)
        {
            Directory.CreateSymbolicLink(path, target);
        }
        else
        {
            File.CreateSymbolicLink(path, target);
        }
    }

    /// <summary>
    /// Links a file, a directory and a missing target into the extensions directory. Callers
    /// carry <see cref="RequiresSymbolicLinkSupportFactAttribute"/> so the privilege links
    /// require is known to be held.
    /// </summary>
    private static void CreateSymbolicLinks(string extensionsDirectory, string outsideFile, string outsideDirectory)
    {
        CreateSymbolicLink(Path.Combine(extensionsDirectory, "file-link-extension.dll"), outsideFile, isDirectory: false);
        CreateSymbolicLink(
            Path.Combine(extensionsDirectory, "dangling-link-extension.dll"),
            Path.Combine(outsideDirectory, "no-such-extension.dll"),
            isDirectory: false);
        CreateSymbolicLink(Path.Combine(extensionsDirectory, "directory-link"), outsideDirectory, isDirectory: true);
    }

    /// <summary>Applies uniform timestamps and, on Unix, permissions to every file in a layout.</summary>
    private static void StampMetadata(string contentRoot, DateTime lastWriteTimeUtc, UnixFileMode mode)
    {
        foreach (string filePath in Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(filePath, lastWriteTimeUtc);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(filePath, mode);
            }
        }
    }

    /// <summary>Reads file entries from an artifact archive.</summary>
    private static IReadOnlyList<ArchiveEntry> ReadArchive(byte[] archive)
    {
        List<ArchiveEntry> entries = [];
        using MemoryStream archiveStream = new(archive, writable: false);
        using TarReader reader = new(archiveStream);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            Stream dataStream = Assert.IsAssignableFrom<Stream>(entry.DataStream);
            using MemoryStream entryContent = new();
            dataStream.CopyTo(entryContent);
            entries.Add(new ArchiveEntry(entry.Name, entryContent.ToArray(), entry.Mode, entry.ModificationTime));
        }

        return entries;
    }

    private static void AssertArchiveEntry(ArchiveEntry entry, string expectedName, byte[] expectedContent)
    {
        Assert.Equal(expectedName, entry.Name);
        Assert.Equal(expectedContent, entry.Content);
        // Fixed rather than inherited, so an extracted extension is never writable by another
        // account and never gains the execute bit from however the deployment was unpacked.
        Assert.Equal(ExpectedEntryMode, entry.Mode);
        Assert.Equal(DateTimeOffset.UnixEpoch, entry.ModificationTime);
    }

    /// <summary>A file entry read back from an artifact archive.</summary>
    private readonly record struct ArchiveEntry(string Name, byte[] Content, UnixFileMode Mode, DateTimeOffset ModificationTime);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"worker-proxy-artifact-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
