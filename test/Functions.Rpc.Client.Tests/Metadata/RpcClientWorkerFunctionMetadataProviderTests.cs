// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class RpcClientWorkerFunctionMetadataProviderTests
{
    private const string HttpTriggerBinding = """{"type":"httpTrigger","name":"req","direction":"in"}""";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private readonly List<WorkerChannel> _channels = [];
    private readonly Mock<IWorkerChannelRegistry> _registry = new();
    private readonly Mock<IWorkerRuntimeResolver> _runtimeResolver = new();

    public RpcClientWorkerFunctionMetadataProviderTests()
    {
        _registry.Setup(registry => registry.GetInitializedChannels())
            .Returns(() => [.. _channels]);
        _registry.Setup(registry => registry.WaitForFirstInitializedAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken cancellationToken) => WaitForChannelAsync(cancellationToken));
        _registry.Setup(registry => registry.UnlinkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _runtimeResolver.Setup(resolver => resolver.GetWorkerRuntime(It.IsAny<string>()))
            .Returns("dotnet-isolated");
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_ReturnsValidatedWorkerMetadata()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();

        Task<FunctionMetadataResult> getMetadata = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await worker.SendFunctionMetadataResponseAsync([CreateMetadata("HttpFunction", "function-id", HttpTriggerBinding)]);
        FunctionMetadataResult result = await getMetadata.WaitAsync(TestTimeout);

        FunctionMetadata function = Assert.Single(result.Functions);
        Assert.False(result.UseDefaultMetadataIndexing);
        Assert.Equal("HttpFunction", function.Name);
        Assert.Equal("dotnet-isolated", function.Language);
        Assert.Equal("httpTrigger", Assert.Single(function.Bindings).Type);
        Assert.Empty(provider.FunctionErrors);
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_WaitsForFirstInitializedChannel()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();

        Task<FunctionMetadataResult> getMetadata = provider.GetFunctionMetadataAsync([]);
        Assert.False(getMetadata.IsCompleted);

        _channels.Add(worker.Channel);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await worker.SendFunctionMetadataResponseAsync([CreateMetadata("HttpFunction", "function-id", HttpTriggerBinding)]);

        Assert.Single((await getMetadata.WaitAsync(TestTimeout)).Functions);
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_NoInitializedChannelTimesOut()
    {
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider(TimeSpan.FromMilliseconds(50));

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(
            () => provider.GetFunctionMetadataAsync([]).WaitAsync(TestTimeout));

        Assert.Contains("No client-backed worker channel initialized", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_DefaultIndexingRequestThrows()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();

        Task<FunctionMetadataResult> firstRequest = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await worker.SendFunctionMetadataResponseAsync(useDefaultMetadataIndexing: true);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => firstRequest.WaitAsync(TestTimeout));

        Assert.Equal(
            "Client-backed workers must provide function metadata and cannot request host metadata indexing.",
            exception.Message);
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_EmptyResultIsCached()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();

        Task<FunctionMetadataResult> firstRequest = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await worker.SendFunctionMetadataResponseAsync();
        FunctionMetadataResult first = await firstRequest.WaitAsync(TestTimeout);
        FunctionMetadataResult second = await provider.GetFunctionMetadataAsync([]);

        Assert.False(first.UseDefaultMetadataIndexing);
        Assert.Empty(first.Functions);
        Assert.Same(first, second);
        Assert.False(worker.Transport.Requests.TryRead(out _));
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_ExposesValidationErrorsWhileRetainingWorkerResults()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();
        RpcFunctionMetadata workerError = CreateMetadata("WorkerError", "worker-error", HttpTriggerBinding);
        workerError.Status = new()
        {
            Status = StatusResult.Types.Status.Failure,
            Exception = new() { Message = "worker indexing failed" },
        };

        Task<FunctionMetadataResult> getMetadata = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await worker.SendFunctionMetadataResponseAsync(
        [
            CreateMetadata("Valid", "valid", HttpTriggerBinding),
            CreateMetadata("Invalid", "invalid", """{"type":"blob","name":"output","direction":"out"}"""),
            workerError,
        ]);
        FunctionMetadataResult result = await getMetadata.WaitAsync(TestTimeout);

        Assert.Equal(["Valid", "WorkerError"],
            result.Functions.Select(function => function.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Contains("Invalid", provider.FunctionErrors.Keys);
        Assert.DoesNotContain("WorkerError", provider.FunctionErrors.Keys);
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_ConcurrentCallersShareCacheFill()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();

        Task<FunctionMetadataResult> firstRequest = provider.GetFunctionMetadataAsync([]);
        Task<FunctionMetadataResult> secondRequest = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await worker.SendFunctionMetadataResponseAsync([CreateMetadata("HttpFunction", "function-id", HttpTriggerBinding)]);
        FunctionMetadataResult[] results = await Task.WhenAll(firstRequest, secondRequest).WaitAsync(TestTimeout);

        Assert.Same(results[0], results[1]);
        Assert.False(worker.Transport.Requests.TryRead(out _));
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_ForceRefreshRebuildsProviderCacheFromChannelResult()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();

        Task<FunctionMetadataResult> firstRequest = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await worker.SendFunctionMetadataResponseAsync([CreateMetadata("First", "first", HttpTriggerBinding)]);
        FunctionMetadataResult first = await firstRequest.WaitAsync(TestTimeout);

        FunctionMetadataResult refreshed = await provider.GetFunctionMetadataAsync([], forceRefresh: true);

        Assert.Equal("First", Assert.Single(first.Functions).Name);
        Assert.Equal("First", Assert.Single(refreshed.Functions).Name);
        Assert.NotSame(first, refreshed);
        Assert.Same(refreshed, await provider.GetFunctionMetadataAsync([]));
        Assert.False(worker.Transport.Requests.TryRead(out _));
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_OverlappingProviderRefreshJoinsPendingChannelRequest()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider firstProvider = CreateProvider();
        RpcClientWorkerFunctionMetadataProvider restartingProvider = CreateProvider();

        Task<FunctionMetadataResult> firstRequest = firstProvider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        Task<FunctionMetadataResult> overlappingRefresh = restartingProvider.GetFunctionMetadataAsync([], forceRefresh: true);
        Assert.False(worker.Transport.Requests.TryRead(out _));

        await worker.SendFunctionMetadataResponseAsync([CreateMetadata("Shared", "shared", HttpTriggerBinding)]);
        FunctionMetadataResult[] results = await Task.WhenAll(firstRequest, overlappingRefresh).WaitAsync(TestTimeout);

        Assert.All(results, result => Assert.Equal("Shared", Assert.Single(result.Functions).Name));
        Assert.NotSame(results[0].Functions[0], results[1].Functions[0]);
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_ChannelFailureDoesNotPoisonProviderOrRegistry()
    {
        await using ClientWorkerChannelTestHarness failed = await ClientWorkerChannelTestHarness.CreateAsync("failed");
        await using ClientWorkerChannelTestHarness replacement = await ClientWorkerChannelTestHarness.CreateAsync("replacement");
        _channels.Add(failed.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider(TimeSpan.FromMilliseconds(50));

        Task<FunctionMetadataResult> failedRequest = provider.GetFunctionMetadataAsync([]);
        await failed.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        InvalidOperationException transportFailure = new("transport failed");
        failed.Transport.CompleteResponses(transportFailure);
        await Assert.ThrowsAnyAsync<Exception>(() => failedRequest.WaitAsync(TestTimeout));

        _channels.Clear();
        _channels.Add(replacement.Channel);
        Task<FunctionMetadataResult> replacementRequest = provider.GetFunctionMetadataAsync([]);
        await replacement.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await replacement.SendFunctionMetadataResponseAsync([CreateMetadata("Recovered", "recovered", HttpTriggerBinding)]);

        Assert.Equal("Recovered", Assert.Single((await replacementRequest.WaitAsync(TestTimeout)).Functions).Name);
        _registry.Verify(registry => registry.UnlinkAsync("failed", It.IsAny<CancellationToken>()), Times.Once);
        _registry.Verify(registry => registry.DisposeAsync(), Times.Never);
    }

    private RpcClientWorkerFunctionMetadataProvider CreateProvider(TimeSpan? channelWaitTimeout = null)
        => new(
            _registry.Object,
            NullLogger<RpcClientWorkerFunctionMetadataProvider>.Instance,
            _runtimeResolver.Object,
            channelWaitTimeout ?? TimeSpan.FromSeconds(10));

    private async Task<WorkerChannel> WaitForChannelAsync(CancellationToken cancellationToken)
    {
        while (_channels.Count == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }

        return _channels[0];
    }

    private static RpcFunctionMetadata CreateMetadata(
        string name,
        string functionId,
        params string[] rawBindings)
    {
        RpcFunctionMetadata metadata = new()
        {
            FunctionId = functionId,
            Language = "worker-language",
            Name = name,
            Status = new() { Status = StatusResult.Types.Status.Success },
        };
        metadata.RawBindings.Add(rawBindings);
        return metadata;
    }
}
