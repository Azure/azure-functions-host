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
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    [Fact]
    public async Task ExtensionPayload_LinkReturnsBeforeTransferAndAnotherValidWorkerCanUnblockStartup()
    {
        await using WorkerExtensionPayloadManager payloads = new(NullLogger<WorkerExtensionPayloadManager>.Instance);
        RegistryHarness harness = new(null, payloads);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<WorkerChannel> initialized = registry.WaitForFirstInitializedAsync();
        Task<WorkerChannel> secondWaiter = registry.WaitForFirstInitializedAsync();
        using CancellationTokenSource cancellation = new();
        Task<WorkerChannel> canceledWaiter = registry.WaitForFirstInitializedAsync(cancellation.Token);
        ChannelControl invalid = new("invalid") { ExtensionPayloadCapability = "1" };
        harness.Enqueue(invalid);
        await registry.LinkAsync(invalid.Id, CreateEndpoint(invalid.Id)).WaitAsync(TestTimeout);
        StreamingMessage rejectedRequest = await invalid.Transport.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Assert.False(initialized.IsCompleted);
        Assert.False(secondWaiter.IsCompleted);
        Assert.False(registry.TryGetInitializedChannel(invalid.Id, out _));
        Assert.Empty(registry.GetInitializedChannels());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWaiter.WaitAsync(TestTimeout));
        await invalid.Transport.SendResponseAsync(new()
        {
            RequestId = rejectedRequest.RequestId,
            ExtensionPayloadResponse = new() { Result = new() { Status = StatusResult.Types.Status.Failure } },
        });
        await invalid.DisposeStarted.WaitAsync(TestTimeout);
        Assert.False(initialized.IsCompleted);
        Assert.False(secondWaiter.IsCompleted);

        ChannelControl valid = new("valid") { ExtensionPayloadCapability = "1" };
        harness.Enqueue(valid);
        await registry.LinkAsync(valid.Id, CreateEndpoint(valid.Id)).WaitAsync(TestTimeout);
        await SupplyPayloadAsync(valid, "assembly");
        Assert.Same(valid.Channel, await initialized.WaitAsync(TestTimeout));
        Assert.Same(valid.Channel, await secondWaiter.WaitAsync(TestTimeout));
        Assert.True(registry.TryGetInitializedChannel(valid.Id, out WorkerChannel channel));
        Assert.Same(valid.Channel, channel);
        Assert.Same(valid.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.True(File.Exists(Path.Combine(payloads.DirectoryPath, "extensions.json")));
        Assert.True(File.Exists(Path.Combine(payloads.DirectoryPath, ".azurefunctions", "binding.dll")));
        RpcClientExtensionPathProvider paths = new(payloads);
        Assert.False(paths.UseExtensionBundles);
        Assert.True(paths.TryResolveExtensionsMetadataPath("unused", out string metadata, out string probing));
        Assert.Equal(payloads.DirectoryPath, metadata);
        Assert.Equal(Path.Combine(payloads.DirectoryPath, ".azurefunctions"), probing);
    }

    [Fact]
    public async Task WaitForFirstInitializedAsync_RequiresTheCompletePayloadBeforePublishingTheChannel()
    {
        await using WorkerExtensionPayloadManager payloads = new(NullLogger<WorkerExtensionPayloadManager>.Instance);
        RegistryHarness harness = new(null, payloads);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<WorkerChannel> initialized = registry.WaitForFirstInitializedAsync();
        ChannelControl worker = new("worker") { ExtensionPayloadCapability = "1" };
        harness.Enqueue(worker);

        WorkerLinkResult link = await registry.LinkAsync(worker.Id, CreateEndpoint(worker.Id)).WaitAsync(TestTimeout);
        StreamingMessage request = await worker.Transport.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Assert.Same(worker.Channel, link.Channel);
        byte[] payload = CreatePayload("assembly");
        await worker.Transport.SendResponseAsync(new()
        {
            RequestId = request.RequestId,
            ExtensionPayloadResponse = new() { Chunk = ByteString.CopyFrom(payload) },
        });

        Assert.False(initialized.IsCompleted);
        Assert.False(registry.TryGetInitializedChannel(worker.Id, out _));
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Null(payloads.DirectoryPath);
        await worker.Transport.SendResponseAsync(new()
        {
            RequestId = request.RequestId,
            ExtensionPayloadResponse = new() { Offset = (ulong)payload.Length, Result = new() { Status = StatusResult.Types.Status.Success } },
        });

        Assert.Same(worker.Channel, await initialized.WaitAsync(TestTimeout));
        Assert.True(registry.TryGetInitializedChannel(worker.Id, out WorkerChannel channel));
        Assert.Same(worker.Channel, channel);
        Assert.Same(worker.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Equal("assembly", await File.ReadAllTextAsync(Path.Combine(payloads.DirectoryPath, ".azurefunctions", "binding.dll")));
    }

    [Fact]
    public async Task WaitForFirstInitializedAsync_NonAdvertisingWorkerUsesTheExistingSourceWithoutATransfer()
    {
        await using WorkerExtensionPayloadManager payloads = new(NullLogger<WorkerExtensionPayloadManager>.Instance);
        RegistryHarness harness = new(null, payloads);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<WorkerChannel> initialized = registry.WaitForFirstInitializedAsync();
        ChannelControl worker = new("worker");
        harness.Enqueue(worker);

        await registry.LinkAsync(worker.Id, CreateEndpoint(worker.Id)).WaitAsync(TestTimeout);

        Assert.Same(worker.Channel, await initialized.WaitAsync(TestTimeout));
        Assert.True(registry.TryGetInitializedChannel(worker.Id, out _));
        Assert.Same(worker.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Null(payloads.DirectoryPath);
        Assert.False(worker.Transport.Requests.TryRead(out _));
    }

    [Fact]
    public async Task ExtensionPathsWithoutASelectedDirectoryPreserveLegacyBehavior()
    {
        await using WorkerExtensionPayloadManager payloads = new(NullLogger<WorkerExtensionPayloadManager>.Instance);
        RpcClientExtensionPathProvider paths = new(payloads);

        Assert.True(paths.UseExtensionBundles);
        Assert.False(paths.TryResolveExtensionsMetadataPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), out _, out _));
    }

    [Fact]
    public async Task ExtensionPayload_UnlinkCancelsPendingTransferAndAllowsShutdown()
    {
        await using WorkerExtensionPayloadManager payloads = new(NullLogger<WorkerExtensionPayloadManager>.Instance);
        RegistryHarness harness = new(null, payloads);
        await using WorkerChannelRegistry registry = harness.Registry;
        ChannelControl worker = new("worker") { ExtensionPayloadCapability = "1" };
        harness.Enqueue(worker);
        await registry.LinkAsync(worker.Id, CreateEndpoint(worker.Id)).WaitAsync(TestTimeout);
        StreamingMessage request = await worker.Transport.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Assert.Equal(StreamingMessage.ContentOneofCase.GetExtensionPayloadRequest, request.ContentCase);

        Assert.True(await registry.UnlinkAsync(worker.Id).WaitAsync(TestTimeout));
        await registry.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        await payloads.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        Assert.False(registry.TryGetInitializedChannel(worker.Id, out _));
        Assert.Null(payloads.DirectoryPath);
    }

    [Fact]
    public async Task ExtensionPayload_ShutdownCancelsActiveAndWaitingCandidates()
    {
        await using WorkerExtensionPayloadManager payloads = new(NullLogger<WorkerExtensionPayloadManager>.Instance);
        RegistryHarness harness = new(null, payloads);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<WorkerChannel> initialized = registry.WaitForFirstInitializedAsync();
        ChannelControl first = new("first") { ExtensionPayloadCapability = "1" };
        ChannelControl second = new("second") { ExtensionPayloadCapability = "1" };
        harness.Enqueue(first);
        harness.Enqueue(second);
        await registry.LinkAsync(first.Id, CreateEndpoint(first.Id)).WaitAsync(TestTimeout);
        StreamingMessage request = await first.Transport.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Assert.Equal(StreamingMessage.ContentOneofCase.GetExtensionPayloadRequest, request.ContentCase);
        await registry.LinkAsync(second.Id, CreateEndpoint(second.Id)).WaitAsync(TestTimeout);

        await registry.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        await payloads.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => initialized.WaitAsync(TestTimeout));
        Assert.Null(payloads.DirectoryPath);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        Assert.False(first.Transport.Requests.TryRead(out _));
        Assert.False(second.Transport.Requests.TryRead(out _));
    }

    [Fact]
    public async Task ExtensionPayload_CancellationDoesNotSendAnotherProtocolMessage()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        ChannelControl worker = new("worker");
        harness.Enqueue(worker);
        await registry.LinkAsync(worker.Id, CreateEndpoint(worker.Id)).WaitAsync(TestTimeout);
        using MemoryStream destination = new();
        using CancellationTokenSource cancellation = new();
        RpcClientWorkerChannel channel = Assert.IsType<RpcClientWorkerChannel>(worker.Channel);
        Task transfer = channel.ReceiveExtensionPayloadAsync(destination, cancellation.Token);
        StreamingMessage request = await worker.Transport.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Assert.Equal(StreamingMessage.ContentOneofCase.GetExtensionPayloadRequest, request.ContentCase);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.WaitAsync(TestTimeout));
        Assert.True(await registry.UnlinkAsync(worker.Id).WaitAsync(TestTimeout));
        Assert.False(worker.Transport.Requests.TryRead(out _));
    }

    [Fact]
    public async Task ExtensionPayload_SubsequentWorkerUsesSelectedSourceWithoutATransfer()
    {
        await using WorkerExtensionPayloadManager payloads = new(NullLogger<WorkerExtensionPayloadManager>.Instance);
        RegistryHarness harness = new(null, payloads);
        await using WorkerChannelRegistry registry = harness.Registry;
        ChannelControl first = new("first") { ExtensionPayloadCapability = "1" };
        harness.Enqueue(first);
        await registry.LinkAsync(first.Id, CreateEndpoint(first.Id)).WaitAsync(TestTimeout);
        await SupplyPayloadAsync(first, "assembly");
        await registry.WaitForFirstInitializedAsync().WaitAsync(TestTimeout);
        string source = payloads.DirectoryPath;

        ChannelControl second = new("second") { ExtensionPayloadCapability = "1" };
        harness.Enqueue(second);
        await registry.LinkAsync(second.Id, CreateEndpoint(second.Id)).WaitAsync(TestTimeout);
        await registry.ActivateAsync([], new(), functionTimeout: null);
        await WaitUntilAsync(() => registry.GetReadyChannelsAsync().IsCompletedSuccessfully &&
            registry.GetReadyChannelsAsync().Result.Any(channel => ReferenceEquals(channel, second.Channel)));

        Assert.False(second.Transport.Requests.TryRead(out _));
        Assert.Equal(source, payloads.DirectoryPath);
        Assert.Equal("assembly", await File.ReadAllTextAsync(Path.Combine(source, ".azurefunctions", "binding.dll")));
        Assert.True(registry.TryGetInitializedChannel(first.Id, out _));
        Assert.True(registry.TryGetInitializedChannel(second.Id, out _));
        Assert.Equal(0, second.DisposeCount);

        Assert.True(await registry.UnlinkAsync(first.Id).WaitAsync(TestTimeout));
        Assert.Equal(source, payloads.DirectoryPath);
        Assert.Same(second.Channel, await registry.WaitForFirstInitializedAsync().WaitAsync(TestTimeout));
        Assert.False(second.Transport.Requests.TryRead(out _));
    }

    [Fact]
    public async Task ExtensionPayload_ConcurrentCandidatesShareTheFirstAcceptedSource()
    {
        await using WorkerExtensionPayloadManager payloads = new(NullLogger<WorkerExtensionPayloadManager>.Instance);
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        ChannelControl first = new("first") { ExtensionPayloadCapability = "1" };
        ChannelControl second = new("second") { ExtensionPayloadCapability = "1" };
        harness.Enqueue(first);
        harness.Enqueue(second);
        await registry.LinkAsync(first.Id, CreateEndpoint(first.Id)).WaitAsync(TestTimeout);
        await registry.LinkAsync(second.Id, CreateEndpoint(second.Id)).WaitAsync(TestTimeout);
        Task firstPreparation = payloads.PrepareAsync(first.Channel, CancellationToken.None);
        StreamingMessage request = await first.Transport.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Task secondPreparation = payloads.PrepareAsync(second.Channel, CancellationToken.None);

        Assert.False(secondPreparation.IsCompleted);
        Assert.False(second.Transport.Requests.TryRead(out _));
        await SendPayloadAsync(first, request, "assembly");
        await Task.WhenAll(firstPreparation, secondPreparation).WaitAsync(TestTimeout);

        Assert.False(second.Transport.Requests.TryRead(out _));
        Assert.Equal("assembly", await File.ReadAllTextAsync(Path.Combine(payloads.DirectoryPath, ".azurefunctions", "binding.dll")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtensionPayload_ConcurrentCandidateRetriesAfterFirstFailure(bool disconnect)
    {
        await using WorkerExtensionPayloadManager payloads = new(NullLogger<WorkerExtensionPayloadManager>.Instance);
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        ChannelControl first = new("first") { ExtensionPayloadCapability = "1" };
        ChannelControl second = new("second") { ExtensionPayloadCapability = "1" };
        harness.Enqueue(first);
        harness.Enqueue(second);
        await registry.LinkAsync(first.Id, CreateEndpoint(first.Id)).WaitAsync(TestTimeout);
        await registry.LinkAsync(second.Id, CreateEndpoint(second.Id)).WaitAsync(TestTimeout);
        Task firstPreparation = payloads.PrepareAsync(first.Channel, CancellationToken.None);
        StreamingMessage request = await first.Transport.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Task secondPreparation = payloads.PrepareAsync(second.Channel, CancellationToken.None);
        if (disconnect)
        {
            first.Complete();
            await Assert.ThrowsAsync<IOException>(() => firstPreparation.WaitAsync(TestTimeout));
        }
        else
        {
            await first.Transport.SendResponseAsync(new()
            {
                RequestId = request.RequestId,
                ExtensionPayloadResponse = new() { Result = new() { Status = StatusResult.Types.Status.Failure } },
            });
            await Assert.ThrowsAsync<InvalidDataException>(() => firstPreparation.WaitAsync(TestTimeout));
        }

        await SupplyPayloadAsync(second, "replacement");
        await secondPreparation.WaitAsync(TestTimeout);

        Assert.Equal("replacement", await File.ReadAllTextAsync(Path.Combine(payloads.DirectoryPath, ".azurefunctions", "binding.dll")));
    }

    [Fact]
    public async Task ExtensionPayload_CanceledCandidateWaitingForSourceDoesNotCancelItsOwner()
    {
        await using WorkerExtensionPayloadManager payloads = new(NullLogger<WorkerExtensionPayloadManager>.Instance);
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        ChannelControl first = new("first") { ExtensionPayloadCapability = "1" };
        ChannelControl second = new("second") { ExtensionPayloadCapability = "1" };
        harness.Enqueue(first);
        harness.Enqueue(second);
        await registry.LinkAsync(first.Id, CreateEndpoint(first.Id)).WaitAsync(TestTimeout);
        await registry.LinkAsync(second.Id, CreateEndpoint(second.Id)).WaitAsync(TestTimeout);
        Task firstPreparation = payloads.PrepareAsync(first.Channel, CancellationToken.None);
        StreamingMessage request = await first.Transport.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        using CancellationTokenSource cancellation = new();
        Task secondPreparation = payloads.PrepareAsync(second.Channel, cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondPreparation.WaitAsync(TestTimeout));
        Assert.False(firstPreparation.IsCompleted);
        Assert.False(second.Transport.Requests.TryRead(out _));
        await SendPayloadAsync(first, request, "assembly");
        await firstPreparation.WaitAsync(TestTimeout);
        await payloads.PrepareAsync(second.Channel, CancellationToken.None).WaitAsync(TestTimeout);

        Assert.False(second.Transport.Requests.TryRead(out _));
        Assert.Equal("assembly", await File.ReadAllTextAsync(Path.Combine(payloads.DirectoryPath, ".azurefunctions", "binding.dll")));
    }

    [Fact]
    public async Task ExtensionPayload_FirstNonAdvertisingWorkerKeepsTheExistingSource()
    {
        await using WorkerExtensionPayloadManager payloads = new(NullLogger<WorkerExtensionPayloadManager>.Instance);
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        ChannelControl first = new("first");
        ChannelControl second = new("second") { ExtensionPayloadCapability = "1" };
        harness.Enqueue(first);
        harness.Enqueue(second);
        await registry.LinkAsync(first.Id, CreateEndpoint(first.Id)).WaitAsync(TestTimeout);
        await registry.LinkAsync(second.Id, CreateEndpoint(second.Id)).WaitAsync(TestTimeout);

        await payloads.PrepareAsync(first.Channel, CancellationToken.None).WaitAsync(TestTimeout);
        await payloads.PrepareAsync(second.Channel, CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Null(payloads.DirectoryPath);
        Assert.False(first.Transport.Requests.TryRead(out _));
        Assert.False(second.Transport.Requests.TryRead(out _));
    }

    private static async Task SupplyPayloadAsync(ChannelControl control, string assembly)
    {
        StreamingMessage request = await control.Transport.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        await SendPayloadAsync(control, request, assembly);
    }

    private static async Task SendPayloadAsync(ChannelControl control, StreamingMessage request, string assembly)
    {
        Assert.Equal(StreamingMessage.ContentOneofCase.GetExtensionPayloadRequest, request.ContentCase);
        byte[] payload = CreatePayload(assembly);
        await control.Transport.SendResponseAsync(new()
        {
            RequestId = request.RequestId,
            ExtensionPayloadResponse = new() { Chunk = ByteString.CopyFrom(payload) },
        });
        await control.Transport.SendResponseAsync(new()
        {
            RequestId = request.RequestId,
            ExtensionPayloadResponse = new() { Offset = (ulong)payload.Length, Result = new() { Status = StatusResult.Types.Status.Success } },
        });
    }

    private static byte[] CreatePayload(string assembly)
    {
        using MemoryStream bytes = new();
        using (ZipArchive archive = new(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (StreamWriter writer = new(archive.CreateEntry("extensions.json").Open(), new UTF8Encoding(false)))
            {
                writer.Write("""{"extensions":[{"typeName":"Binding.Startup, binding","hintPath":"./.azurefunctions/binding.dll"}]}""");
            }

            using StreamWriter content = new(archive.CreateEntry(".azurefunctions/binding.dll").Open(), new UTF8Encoding(false));
            content.Write(assembly);
        }

        return bytes.ToArray();
    }
}
