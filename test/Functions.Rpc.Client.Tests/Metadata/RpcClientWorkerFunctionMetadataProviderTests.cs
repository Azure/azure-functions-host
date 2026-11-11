// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    private TaskCompletionSource _channelChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RpcClientWorkerFunctionMetadataProviderTests()
    {
        _registry.Setup(registry => registry.GetInitializedChannels())
            .Returns(() => [.. _channels]);
        _registry.Setup(registry => registry.GetChangeTask())
            .Returns(() => _channelChanged.Task);
        _registry.Setup(registry => registry.UnlinkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _runtimeResolver.Setup(resolver => resolver.GetWorkerRuntime(It.IsAny<string>()))
            .Returns("dotnet-isolated");
    }

    [Theory]
    [InlineData(true, "worker-language")]
    [InlineData(false, "dotnet-isolated")]
    public async Task GetFunctionMetadataAsync_ReturnsValidatedMetadataUsingRuntimeAuthorityOption(
        bool useWorkerRuntimeFromMetadata, string expectedLanguage)
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider(useWorkerRuntimeFromMetadata: useWorkerRuntimeFromMetadata);

        Task<FunctionMetadataResult> getMetadata = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await worker.SendFunctionMetadataResponseAsync([CreateMetadata("HttpFunction", "function-id", HttpTriggerBinding)]);
        FunctionMetadataResult result = await getMetadata.WaitAsync(TestTimeout);

        FunctionMetadata function = Assert.Single(result.Functions);
        Assert.False(result.UseDefaultMetadataIndexing);
        Assert.Equal("HttpFunction", function.Name);
        Assert.Equal(expectedLanguage, function.Language);
        Assert.Equal("httpTrigger", Assert.Single(function.Bindings).Type);
        Assert.Empty(provider.FunctionErrors);
        _runtimeResolver.Verify(resolver => resolver.GetWorkerRuntime(It.IsAny<string>()),
            useWorkerRuntimeFromMetadata ? Times.Never() : Times.Once());
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_PreservesWholeAppGroupsThroughValidationAndCache()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "http-indexer", functionGroupName: "http", maxHttpConcurrency: "16");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();
        RpcFunctionMetadata queue = CreateMetadata("QueueProcessor", "queue", """{"type":"queueTrigger","name":"item","direction":"in"}""");
        queue.Properties.Add("FunctionGroupName", "http");
        Task<FunctionMetadataResult> request = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await worker.SendFunctionMetadataResponseAsync(
        [
            CreateMetadata("Http", "http", HttpTriggerBinding),
            CreateMetadata("Durable", "durable", """{"type":"orchestrationTrigger","name":"context","direction":"in"}"""),
            CreateMetadata("Blob", "blob", """{"type":"blobTrigger","source":"EventGrid","name":"item","direction":"in"}"""),
            queue,
            CreateMetadata("LegacyBlob", "legacy", """{"type":"blobTrigger","name":"item","direction":"in"}"""),
        ]);
        FunctionMetadataResult result = await request.WaitAsync(TestTimeout);

        Assert.Equal(["http", "durable", "blob", "function:QueueProcessor"],
            result.Functions.Select(function => function.GetFunctionGroup()));
        Assert.Equal("http", result.Functions[3].Properties["FunctionGroupName"]);
        Assert.Contains("LegacyBlob", provider.FunctionErrors.Keys);
        Assert.Same(result, await provider.GetFunctionMetadataAsync([]));
        Assert.Equal(["http", "durable", "blob", "function:QueueProcessor"],
            (await provider.GetFunctionMetadataAsync([])).Functions.Select(function => function.GetFunctionGroup()));
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_WaitsForFirstInitializedChannel()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();

        Task<FunctionMetadataResult> getMetadata = provider.GetFunctionMetadataAsync([]);
        Assert.False(getMetadata.IsCompleted);

        _channels.Add(worker.Channel);
        SignalChannelChange();
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await worker.SendFunctionMetadataResponseAsync([CreateMetadata("HttpFunction", "function-id", HttpTriggerBinding)]);

        Assert.Single((await getMetadata.WaitAsync(TestTimeout)).Functions);
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_WaitsForNonDrainingChannel()
    {
        await using ClientWorkerChannelTestHarness draining = await ClientWorkerChannelTestHarness.CreateAsync("draining");
        await using ClientWorkerChannelTestHarness replacement = await ClientWorkerChannelTestHarness.CreateAsync("replacement");
        _channels.Add(draining.Channel);
        draining.Channel.StopAcceptingInvocations();
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();

        Task<FunctionMetadataResult> getMetadata = provider.GetFunctionMetadataAsync([]);

        Assert.False(getMetadata.IsCompleted);
        Assert.False(draining.Transport.Requests.TryRead(out _));

        _channels.Add(replacement.Channel);
        SignalChannelChange();
        await replacement.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await replacement.SendFunctionMetadataResponseAsync([CreateMetadata("HttpFunction", "function-id", HttpTriggerBinding)]);

        Assert.Single((await getMetadata.WaitAsync(TestTimeout)).Functions);
        Assert.False(draining.Transport.Requests.TryRead(out _));
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
    public async Task GetFunctionMetadataAsync_MetadataRequestCanOutlastChannelWaitTimeout()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider(TimeSpan.FromMilliseconds(50));

        Task<FunctionMetadataResult> getMetadata = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        await worker.SendFunctionMetadataResponseAsync([CreateMetadata("HttpFunction", "function-id", HttpTriggerBinding)]);

        Assert.Single((await getMetadata.WaitAsync(TestTimeout)).Functions);
        _registry.Verify(registry => registry.UnlinkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, worker.Transport.DisposeCount);
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_ChannelShutdownUnlinksChannel()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider(TimeSpan.FromMilliseconds(50));

        Task<FunctionMetadataResult> getMetadata = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        Task<List<RawFunctionMetadata>> channelMetadata = worker.Channel.GetFunctionMetadata();
        TimeoutException channelFailure = new("Metadata request timed out.");

        worker.Channel.Shutdown(channelFailure);

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => getMetadata.WaitAsync(TestTimeout));
        Assert.Same(channelFailure, exception);
        Assert.True(channelMetadata.IsFaulted);
        Assert.Same(exception, await Assert.ThrowsAsync<TimeoutException>(() => channelMetadata));
        _registry.Verify(registry => registry.UnlinkAsync("worker", It.IsAny<CancellationToken>()), Times.Once);
        _registry.Verify(registry => registry.DisposeAsync(), Times.Never);
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_RequestErrorUnlinksWithoutPublishingWorkerError()
    {
        Mock<IScriptEventManager> eventManager = new();
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker", eventManager.Object);
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();
        RpcFunctionMetadata metadata = CreateMetadata("HttpFunction", "function-id", HttpTriggerBinding);
        metadata.RetryOptions = new() { RetryStrategy = (RpcRetryOptions.Types.RetryStrategy)(-1) };

        Task<FunctionMetadataResult> getMetadata = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await worker.SendFunctionMetadataResponseAsync([metadata]);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => getMetadata.WaitAsync(TestTimeout));
        Assert.Equal("Unknown RetryStrategy RpcDataType: -1.", exception.Message);
        _registry.Verify(registry => registry.UnlinkAsync("worker", It.IsAny<CancellationToken>()), Times.Once);
        eventManager.Verify(manager => manager.Publish(It.IsAny<WorkerErrorEvent>()), Times.Never);
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
    public async Task GetFunctionMetadataAsync_RejectsPollingBlobTriggers()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();

        Task<FunctionMetadataResult> getMetadata = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await worker.SendFunctionMetadataResponseAsync(
        [
            CreateMetadata("Polling", "polling", """{"type":"blobTrigger","name":"blob","direction":"in","path":"c/{name}"}"""),
            CreateMetadata("LogsAndContainerScan", "scan",
                """{"type":"blobTrigger","name":"blob","direction":"in","path":"c/{name}","source":"LogsAndContainerScan"}"""),
            CreateMetadata("EventGrid", "eventgrid",
                """{"type":"blobTrigger","name":"blob","direction":"in","path":"c/{name}","source":"EventGrid"}"""),
        ]);
        FunctionMetadataResult result = await getMetadata.WaitAsync(TestTimeout);

        FunctionMetadata function = Assert.Single(result.Functions);
        Assert.Equal("EventGrid", function.Name);
        Assert.Equal(["LogsAndContainerScan", "Polling"], provider.FunctionErrors.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("https://aka.ms/blob-trigger-eg", Assert.Single(provider.FunctionErrors["Polling"]), StringComparison.Ordinal);
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
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => failedRequest.WaitAsync(TestTimeout));
        Assert.Same(transportFailure, exception);

        _channels.Clear();
        _channels.Add(replacement.Channel);
        Task<FunctionMetadataResult> replacementRequest = provider.GetFunctionMetadataAsync([]);
        await replacement.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        await replacement.SendFunctionMetadataResponseAsync([CreateMetadata("Recovered", "recovered", HttpTriggerBinding)]);

        Assert.Equal("Recovered", Assert.Single((await replacementRequest.WaitAsync(TestTimeout)).Functions).Name);
        _registry.Verify(registry => registry.UnlinkAsync("failed", It.IsAny<CancellationToken>()), Times.Once);
        _registry.Verify(registry => registry.DisposeAsync(), Times.Never);
    }

    [Fact]
    public async Task GetFunctionMetadataAsync_ChannelFailurePreservesUnlinkFailure()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientWorkerFunctionMetadataProvider provider = CreateProvider();
        InvalidOperationException transportFailure = new("transport failed");
        InvalidOperationException unlinkFailure = new("unlink failed");
        _registry.Setup(registry => registry.UnlinkAsync("worker", It.IsAny<CancellationToken>()))
            .ThrowsAsync(unlinkFailure);

        Task<FunctionMetadataResult> getMetadata = provider.GetFunctionMetadataAsync([]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);
        worker.Transport.CompleteResponses(transportFailure);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => getMetadata.WaitAsync(TestTimeout));
        Assert.Collection(exception.InnerExceptions,
            failure => Assert.Same(transportFailure, failure),
            failure => Assert.Same(unlinkFailure, failure));
    }

    private RpcClientWorkerFunctionMetadataProvider CreateProvider(
        TimeSpan? channelWaitTimeout = null, bool useWorkerRuntimeFromMetadata = true)
        => new(
            _registry.Object,
            NullLogger<RpcClientWorkerFunctionMetadataProvider>.Instance,
            _runtimeResolver.Object,
            Options.Create(new FunctionMetadataOptions { UseWorkerRuntimeFromMetadata = useWorkerRuntimeFromMetadata }),
            channelWaitTimeout ?? TimeSpan.FromSeconds(10));

    private void SignalChannelChange()
    {
        TaskCompletionSource previous = _channelChanged;
        _channelChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
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
