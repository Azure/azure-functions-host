// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class ExtensionPayloadTests
{
    private const string Manifest = """{"extensions":[{"name":"Binding","typeName":"Binding.Startup, binding","hintPath":"./.azurefunctions/binding.dll"}]}""";

    [Fact]
    public async Task Extraction_PreservesManifestAndContents()
    {
        using PayloadDirectory root = new();
        string zip = root.WriteZip("payload.zip", Manifest, "assembly");
        string content = Path.Combine(root.Path, "content");

        await WorkerExtensionPayloadManager.ExtractAsync(zip, content, CancellationToken.None);

        Assert.Equal(Manifest, await File.ReadAllTextAsync(Path.Combine(content, "extensions.json")));
        Assert.Equal("assembly", await File.ReadAllTextAsync(Path.Combine(content, ".azurefunctions", "binding.dll")));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("../content-sibling/escape")]
    [InlineData(".azurefunctions/../../escape")]
    [InlineData(".azurefunctions\\..\\..\\escape")]
    [InlineData("/rooted")]
    [InlineData("C:/rooted")]
    [InlineData(".azurefunctions/file:stream")]
    public async Task Extraction_RejectsPathsOutsideStagingAndAlternateStreams(string name)
    {
        using PayloadDirectory root = new();
        string zip = root.WriteZip("payload.zip", Manifest, "assembly");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.CreateEntry(name);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => WorkerExtensionPayloadManager.ExtractAsync(
            zip, Path.Combine(root.Path, "content"), CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(root.Path, "escape")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "content-sibling")));
    }

    [Fact]
    public async Task Extraction_DoesNotOverwriteDuplicateFiles()
    {
        using PayloadDirectory root = new();
        string zip = root.WriteZip("duplicate.zip", Manifest, "assembly");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.CreateEntry(".azurefunctions/binding.dll");
        }

        await Assert.ThrowsAsync<IOException>(() => WorkerExtensionPayloadManager.ExtractAsync(
            zip, Path.Combine(root.Path, "content"), CancellationToken.None));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{}""")]
    [InlineData("""{"extensions":{}}""")]
    [InlineData("""{"extensions":[{}]}""")]
    [InlineData("""{"extensions":[{"typeName":"not qualified"}]}""")]
    [InlineData("""{"extensions":[{"typeName":"Binding.Startup, binding","hintPath":"../binding.dll"}]}""")]
    [InlineData("""{"extensions":[{"typeName":"Binding.Startup, binding","hintPath":".azurefunctions/missing.dll"}]}""")]
    public async Task Extraction_LeavesManifestInterpretationToTheExistingLoader(string manifest)
    {
        using PayloadDirectory root = new();
        string zip = root.WriteZip("payload.zip", manifest, "assembly");
        string content = Path.Combine(root.Path, "content");

        await WorkerExtensionPayloadManager.ExtractAsync(zip, content, CancellationToken.None);

        Assert.Equal(manifest, await File.ReadAllTextAsync(Path.Combine(content, "extensions.json")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Extraction_RequiresManifestAndExtensionDirectory(bool missingManifest)
    {
        using PayloadDirectory root = new();
        string zip = root.WriteZip("payload.zip", Manifest, "assembly");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.GetEntry(missingManifest ? "extensions.json" : ".azurefunctions/binding.dll").Delete();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => WorkerExtensionPayloadManager.ExtractAsync(
            zip, Path.Combine(root.Path, "content"), CancellationToken.None));
    }

    [Fact]
    public async Task Extraction_PropagatesStandardZipErrors()
    {
        using PayloadDirectory root = new();
        string zip = root.WriteZip("payload.zip", Manifest, "assembly");
        await File.WriteAllTextAsync(zip, "not a ZIP archive");

        await Assert.ThrowsAsync<InvalidDataException>(() => WorkerExtensionPayloadManager.ExtractAsync(
            zip, Path.Combine(root.Path, "content"), CancellationToken.None));
    }

    [Fact]
    public async Task Extraction_EnforcesCompressedExpandedAndEntryBoundaries()
    {
        Assert.Equal(268435456L, WorkerExtensionPayloadManager.MaxCompressedBytes);
        Assert.Equal(1073741824L, WorkerExtensionPayloadManager.MaxExpandedBytes);
        Assert.Equal(10000, WorkerExtensionPayloadManager.MaxEntries);
        using PayloadDirectory root = new();
        string zip = root.WriteZip("payload.zip", Manifest, "assembly");
        long compressed = new FileInfo(zip).Length;
        long expanded = Encoding.UTF8.GetByteCount(Manifest) + Encoding.UTF8.GetByteCount("assembly");

        await WorkerExtensionPayloadManager.ExtractAsync(zip, Path.Combine(root.Path, "exact"), CancellationToken.None, compressed, expanded, 3);
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkerExtensionPayloadManager.ExtractAsync(
            zip, Path.Combine(root.Path, "compressed"), CancellationToken.None, compressed - 1, expanded, 3));
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkerExtensionPayloadManager.ExtractAsync(
            zip, Path.Combine(root.Path, "expanded"), CancellationToken.None, compressed, expanded - 1, 3));
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkerExtensionPayloadManager.ExtractAsync(
            zip, Path.Combine(root.Path, "entries"), CancellationToken.None, compressed, expanded, 2));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task Extraction_BoundsManifestBytesWithoutParsing(int extraBytes, bool accepted)
    {
        using PayloadDirectory root = new();
        string manifest = Manifest.PadRight(WorkerExtensionPayloadManager.MaxManifestBytes + extraBytes);
        string zip = root.WriteZip("manifest.zip", manifest, "assembly");
        Task extraction = WorkerExtensionPayloadManager.ExtractAsync(zip, Path.Combine(root.Path, "content"), CancellationToken.None);
        if (accepted)
        {
            await extraction;
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => extraction);
        }
    }

    [Fact]
    public async Task Extraction_CountsImplicitParentDirectoriesAgainstTheFilesystemBudget()
    {
        using PayloadDirectory root = new();
        string zip = root.WriteZip("parents.zip", Manifest, "assembly");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.CreateEntry(".azurefunctions/one/a/b/file");
            archive.CreateEntry(".azurefunctions/two/a/b/file");
        }

        string content = Path.Combine(root.Path, "content");
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkerExtensionPayloadManager.ExtractAsync(
            zip, content, CancellationToken.None, maxEntries: 10));
        Assert.InRange(Directory.EnumerateFileSystemEntries(content, "*", SearchOption.AllDirectories).Count(), 0, 10);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public async Task Extraction_BoundsPathDepth(int segments, bool accepted)
    {
        using PayloadDirectory root = new();
        string zip = root.WriteZip("depth.zip", Manifest, "assembly");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.CreateEntry(".azurefunctions/" + string.Join('/', Enumerable.Repeat("a", segments - 2)) + "/file");
        }

        Task extraction = WorkerExtensionPayloadManager.ExtractAsync(zip, Path.Combine(root.Path, "content"), CancellationToken.None);
        if (accepted)
        {
            await extraction;
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => extraction);
        }
    }

    [Fact]
    public async Task Extraction_ObservesCancellationBeforeWritingFiles()
    {
        using PayloadDirectory root = new();
        string zip = root.WriteZip("payload.zip", Manifest, "assembly");
        string content = Path.Combine(root.Path, "content");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkerExtensionPayloadManager.ExtractAsync(zip, content, cancellation.Token));

        Assert.False(Directory.Exists(content));
    }

    [Fact]
    public async Task Receiver_AwaitsTheDestinationBeforeConsumingAnotherChunk()
    {
        TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<Stream> destination = new();
        destination.Setup(stream => stream.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask(released.Task));
        ExtensionPayloadReceiver receiver = new(destination.Object, CancellationToken.None);
        Task write = receiver.ReceiveAsync(new() { Chunk = ByteString.CopyFrom([1, 2, 3]) });
        Task stop = receiver.StopAsync();

        Assert.False(write.IsCompleted);
        Assert.False(stop.IsCompleted);
        released.SetResult();
        await Task.WhenAll(write, stop);
        destination.Verify(stream => stream.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Receiver_RequiresOrderedNonemptyChunksAndExplicitTerminalSuccess()
    {
        using MemoryStream destination = new();
        ExtensionPayloadReceiver receiver = new(destination, CancellationToken.None);
        await receiver.ReceiveAsync(new() { Chunk = ByteString.CopyFrom([1, 2, 3]) });
        Assert.False(receiver.Completion.IsCompleted);
        await receiver.ReceiveAsync(new() { Offset = 3, Result = new() { Status = StatusResult.Types.Status.Success } });
        await receiver.Completion;
        Assert.Equal(new byte[] { 1, 2, 3 }, destination.ToArray());

        ExtensionPayloadReceiver invalid = new(destination, CancellationToken.None);
        await invalid.ReceiveAsync(new() { Offset = 1, Chunk = ByteString.CopyFrom([4]) });
        await Assert.ThrowsAsync<InvalidDataException>(() => invalid.Completion);
    }

    [Fact]
    public async Task Receiver_RejectsOverlargeChunksAndFailureResults()
    {
        using MemoryStream destination = new();
        ExtensionPayloadReceiver large = new(destination, CancellationToken.None);
        await large.ReceiveAsync(new() { Chunk = ByteString.CopyFrom(new byte[WorkerExtensionPayloadManager.MaxChunkBytes + 1]) });
        await Assert.ThrowsAsync<InvalidDataException>(() => large.Completion);
        Assert.Equal(0, destination.Length);

        ExtensionPayloadReceiver failed = new(destination, CancellationToken.None);
        await failed.ReceiveAsync(new() { Result = new() { Status = StatusResult.Types.Status.Failure } });
        await Assert.ThrowsAsync<InvalidDataException>(() => failed.Completion);
    }

    private sealed class PayloadDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "host-extension-tests-" + Guid.NewGuid().ToString("N"));

        internal string WriteZip(string name, string manifest, string assembly)
        {
            Directory.CreateDirectory(Path);
            string path = System.IO.Path.Combine(Path, name);
            using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
            Write("extensions.json", manifest);
            Write(".azurefunctions/binding.dll", assembly);
            return path;

            void Write(string entryName, string content)
            {
                ZipArchiveEntry entry = archive.CreateEntry(entryName);
                using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
