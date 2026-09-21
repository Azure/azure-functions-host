// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Rpc;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relay_NormalInitialization_RewritesOnlyWorkerCapabilities(bool runtimeConnectsFirst)
    {
        const string proxyEndpoint = "https://worker-pod.example:48801/";
        await using WorkerProxyWebApplicationFactory factory = CreateHttpCapabilityFactory(proxyEndpoint);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        StreamingMessage start = new() { StartStream = new() { WorkerId = "worker-id" } };
        StreamingMessage init = new()
        {
            RequestId = "runtime-init",
            WorkerInitRequest = new() { HostVersion = "test-host", FunctionAppDirectory = "app" }
        };

        if (runtimeConnectsFirst)
        {
            await runtime.WriteAsync(init, timeout.Token);
            await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
            await worker.WriteAsync(start, timeout.Token);
        }
        else
        {
            await worker.WriteAsync(start, timeout.Token);
            await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Worker, timeout.Token);
            await runtime.WriteAsync(init, timeout.Token);
        }

        Assert.Equal(start, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(init, await worker.ReadAsync(timeout.Token));
        StreamingMessage response = CreateInitResponse("runtime-init", "http://localhost:1234/worker/");
        StreamingMessage expected = response.Clone();
        expected.WorkerInitResponse.Capabilities["HttpUri"] = proxyEndpoint;

        await worker.WriteAsync(response, timeout.Token);

        Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(new Uri("http://localhost:1234/worker/"), relay.WorkerHttpDestination);
    }

    [Theory]
    [InlineData(StatusResult.Types.Status.Failure)]
    [InlineData(StatusResult.Types.Status.Cancelled)]
    [InlineData(null)]
    public async Task Relay_UnsuccessfulInitialization_DoesNotCaptureOrRewriteCapabilities(StatusResult.Types.Status? status)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await runtime.WriteAsync(CreateMessage("attach"), timeout.Token);
        StreamingMessage response = CreateInitResponse("failed-init", "http://localhost:1234");
        response.WorkerInitResponse.Result = status is { } result ? new() { Status = result } : null;

        await worker.WriteAsync(response, timeout.Token);

        Assert.Equal(response, await runtime.ReadAsync(timeout.Token));
        Assert.Null(relay.WorkerHttpDestination);
    }

    [Fact]
    public async Task Relay_RuntimeMessages_AreNotCapabilityFinalized()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await worker.WriteAsync(CreateMessage("attach"), timeout.Token);
        StreamingMessage message = CreateInitResponse("runtime-message", "http://localhost:1234");

        await runtime.WriteAsync(message, timeout.Token);

        Assert.Equal(message, await worker.ReadAsync(timeout.Token));
        Assert.Null(relay.WorkerHttpDestination);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:1234/worker/")]
    public async Task Relay_FinalizesCapabilitiesOnceOnAnOwnedCopy(string? firstHttpUri)
    {
        WorkerProxyOptions options = new() { HttpProxyEndpoint = "https://worker-pod.example:48801/" };
        await using FunctionRpcRelay relay = new(NullLogger<FunctionRpcRelay>.Instance, CreateCapabilityProvider(options));
        using CancellationTokenSource timeout = new(TestTimeout);
        Channel<StreamingMessage> inbound = Channel.CreateUnbounded<StreamingMessage>();
        Channel<StreamingMessage> outbound = Channel.CreateUnbounded<StreamingMessage>();
        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(
            FunctionRpcRelaySide.Runtime, new BlockingStreamReader(), CreateMessageWriter(outbound.Writer), timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(
            FunctionRpcRelaySide.Worker, CreateMessageReader(inbound.Reader), new TestServerStreamWriter(), timeout.Token);
        StreamingMessage first = CreateInitResponse("first-init", firstHttpUri);
        StreamingMessage expected = first.Clone();
        if (firstHttpUri is not null)
        {
            expected.WorkerInitResponse.Capabilities["HttpUri"] = options.HttpProxyEndpoint;
        }

        await inbound.Writer.WriteAsync(first, timeout.Token);
        StreamingMessage finalized = await outbound.Reader.ReadAsync(timeout.Token);

        Assert.NotSame(first, finalized);
        Assert.Equal(expected, finalized);
        Assert.Equal(firstHttpUri is null ? null : new Uri(firstHttpUri), relay.WorkerHttpDestination);
        Assert.Equal(firstHttpUri, first.WorkerInitResponse.Capabilities.GetValueOrDefault("HttpUri"));

        // A second finalization would fail without a proxy origin. Repeated responses must instead use the frozen capabilities.
        options.HttpProxyEndpoint = null;
        first.WorkerInitResponse.Capabilities.Clear();
        finalized.WorkerInitResponse.Capabilities.Clear();
        StreamingMessage repeated = CreateInitResponse("second-init", "http://localhost:5678");
        repeated.WorkerInitResponse.Capabilities["WorkerIndexing"] = "changed";
        StreamingMessage repeatedExpected = repeated.Clone();
        repeatedExpected.WorkerInitResponse.Capabilities.Clear();
        repeatedExpected.WorkerInitResponse.Capabilities.Add(expected.WorkerInitResponse.Capabilities);
        await inbound.Writer.WriteAsync(repeated, timeout.Token);

        Assert.Equal(repeatedExpected, await outbound.Reader.ReadAsync(timeout.Token));
        Assert.Equal(firstHttpUri is null ? null : new Uri(firstHttpUri), relay.WorkerHttpDestination);
        await relay.StopAsync(timeout.Token);
        await Task.WhenAll(runtimeTask, workerTask).WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task Relay_MissingAdvertisedProxyOrigin_TerminatesWithoutPublishingDestination()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);

        await worker.WriteAsync(CreateInitResponse("init", "http://localhost:1234"), timeout.Token);

        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
        await WaitForReleaseAsync(relay, timeout.Token);
        Assert.Null(relay.WorkerHttpDestination);
        Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, relay.LastTerminalState?.Reason);
        InvalidOperationException exception = Assert.IsType<InvalidOperationException>(relay.LastTerminalState?.Exception);
        Assert.Contains(nameof(WorkerProxyOptions.HttpProxyEndpoint), exception.Message);
    }

    [Theory]
    [InlineData("relative", null)]
    [InlineData("http://localhost:1234", "ftp://override:5678")]
    public async Task Relay_InvalidHttpConfiguration_FaultsInsteadOfAdvertisingGrpcFallback(string advertisedEndpoint, string? overrideEndpoint)
    {
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.HttpProxyEndpoint)}"] = "http://worker-pod:28080/",
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] = overrideEndpoint
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);

        await worker.WriteAsync(CreateInitResponse("init", advertisedEndpoint), timeout.Token);

        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
        await WaitForReleaseAsync(relay, timeout.Token);
        Assert.Null(relay.WorkerHttpDestination);
        Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, relay.LastTerminalState?.Reason);
        Assert.IsType<InvalidOperationException>(relay.LastTerminalState?.Exception);
    }

    [Fact]
    public async Task Relay_BlockedCapabilityLogging_DoesNotBlockShutdown()
    {
        using BlockingLogger<WorkerHttpCapabilityProvider> logger = new();
        WorkerHttpCapabilityProvider provider = new(Options.Create(new WorkerProxyOptions()), logger);
        await using FunctionRpcRelay relay = new(NullLogger<FunctionRpcRelay>.Instance, provider);
        using CancellationTokenSource timeout = new(TestTimeout);
        Channel<StreamingMessage> inbound = Channel.CreateUnbounded<StreamingMessage>();
        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(
            FunctionRpcRelaySide.Runtime, new BlockingStreamReader(), new TestServerStreamWriter(), timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(
            FunctionRpcRelaySide.Worker, CreateMessageReader(inbound.Reader), new TestServerStreamWriter(), timeout.Token);
        await inbound.Writer.WriteAsync(CreateInitResponse("init", "invalid"), timeout.Token);

        try
        {
            await logger.LogEntered.WaitAsync(timeout.Token);
            await Task.Run(() => relay.StopAsync(timeout.Token), timeout.Token).WaitAsync(timeout.Token);
            FunctionRpcRelayTerminalState[] terminalStates = await Task.WhenAll(runtimeTask, workerTask).WaitAsync(timeout.Token);
            Assert.All(terminalStates, static state => Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, state.Reason));
            Assert.Null(relay.WorkerHttpDestination);
        }
        finally
        {
            logger.Release();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:5678")]
    public async Task Relay_ReplacementSession_DoesNotReusePreviousDestination(string? replacementEndpoint)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateHttpCapabilityFactory("http://worker-pod:28080/");
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);

        await using (RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token))
        await using (RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token))
        {
            await ExchangeAsync(runtime, worker, "first", timeout.Token);
            await worker.WriteAsync(CreateInitResponse("first-init", "http://localhost:1234"), timeout.Token);
            await runtime.ReadAsync(timeout.Token);
            Assert.Equal(new Uri("http://localhost:1234"), relay.WorkerHttpDestination);
            await runtime.CompleteRequestAsync(timeout.Token);
            await Task.WhenAll(runtime.WaitForTerminationAsync(timeout.Token), worker.WaitForTerminationAsync(timeout.Token));
        }

        await WaitForReleaseAsync(relay, timeout.Token);
        Assert.Null(relay.WorkerHttpDestination);
        await using RelayClient replacementRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient replacementWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(replacementRuntime, replacementWorker, "replacement", timeout.Token);
        Assert.Null(relay.WorkerHttpDestination);

        await replacementWorker.WriteAsync(CreateInitResponse("replacement-init", replacementEndpoint), timeout.Token);
        StreamingMessage response = await replacementRuntime.ReadAsync(timeout.Token);

        Assert.Equal(replacementEndpoint is null ? null : new Uri(replacementEndpoint), relay.WorkerHttpDestination);
        Assert.Equal(replacementEndpoint is not null, response.WorkerInitResponse.Capabilities.ContainsKey("HttpUri"));
    }

    [Fact]
    public async Task Relay_TerminalSession_MakesHttpUnavailableBeforeAttachmentsRelease()
    {
        using BlockingLogger<FunctionRpcRelay> logger = new();
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.HttpProxyEndpoint)}"] = "http://worker-pod:28080/"
        };
        await using WorkerProxyWebApplicationFactory factory = new(
            configuration, services => services.AddSingleton<Microsoft.Extensions.Logging.ILogger<FunctionRpcRelay>>(logger));
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        await worker.WriteAsync(CreateInitResponse("init", "http://localhost:1234"), timeout.Token);
        await runtime.ReadAsync(timeout.Token);

        await runtime.CompleteRequestAsync(timeout.Token);
        try
        {
            await logger.LogEntered.WaitAsync(timeout.Token);
            Assert.Null(relay.WorkerHttpDestination);
            using HttpClient client = factory.CreateHttpForwardingClient();
            using HttpResponseMessage response = await client.GetAsync("/invoke", timeout.Token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            logger.Release();
        }
    }

    [Fact]
    public async Task Relay_AdvertisedHttpUri_ForwardsThroughProxyWithoutDestinationOverride()
    {
        const string correlationId = "http-rpc-invocation";
        await using WebApplication workerApp = await WorkerHttpForwardingTests.StartWorkerAsync(async context =>
        {
            Assert.Equal("/worker/invoke", context.Request.Path);
            Assert.Equal("?name=test", context.Request.QueryString.Value);
            Assert.Equal(correlationId, context.Request.Headers["x-ms-invocation-id"]);
            string body = await new StreamReader(context.Request.Body).ReadToEndAsync(context.RequestAborted);
            context.Response.Headers["x-worker"] = "true";
            await context.Response.WriteAsync($"received:{body}", context.RequestAborted);
        });
        Uri destination = new(WorkerHttpForwardingTests.GetAddress(workerApp), "/worker/");
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using HttpClient proxyClient = factory.CreateHttpForwardingClient();
        string proxyEndpoint = proxyClient.BaseAddress!.AbsoluteUri;
        // The test deployment learns its externally reachable origin after the ephemeral listener binds.
        factory.Services.GetRequiredService<IOptions<WorkerProxyOptions>>().Value.HttpProxyEndpoint = proxyEndpoint;
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        await worker.WriteAsync(CreateInitResponse("init", destination.AbsoluteUri), timeout.Token);
        StreamingMessage initialized = await runtime.ReadAsync(timeout.Token);
        Assert.Equal(proxyEndpoint, initialized.WorkerInitResponse.Capabilities["HttpUri"]);

        using HttpClient client = new() { BaseAddress = new(initialized.WorkerInitResponse.Capabilities["HttpUri"]) };
        using HttpRequestMessage request = new(HttpMethod.Post, "/invoke?name=test")
        {
            Content = new StringContent("payload", Encoding.UTF8, "text/plain")
        };
        request.Headers.Add("x-ms-invocation-id", correlationId);
        Task<HttpResponseMessage> httpTask = client.SendAsync(request, timeout.Token);
        StreamingMessage invocation = new()
        {
            RequestId = "invocation-request",
            InvocationRequest = new() { InvocationId = correlationId }
        };
        await runtime.WriteAsync(invocation, timeout.Token);
        Assert.Equal(invocation, await worker.ReadAsync(timeout.Token));
        StreamingMessage invocationResult = new()
        {
            RequestId = invocation.RequestId,
            InvocationResponse = new() { InvocationId = correlationId, Result = new() { Status = StatusResult.Types.Status.Success } }
        };
        await worker.WriteAsync(invocationResult, timeout.Token);

        Assert.Equal(invocationResult, await runtime.ReadAsync(timeout.Token));
        using HttpResponseMessage response = await httpTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.True(response.Headers.Contains("x-worker"));
        Assert.Equal("received:payload", await response.Content.ReadAsStringAsync(timeout.Token));
    }

    private static WorkerProxyWebApplicationFactory CreateHttpCapabilityFactory(string proxyEndpoint)
    {
        return new(new Dictionary<string, string?>
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.HttpProxyEndpoint)}"] = proxyEndpoint
        });
    }

    private static StreamingMessage CreateInitResponse(string requestId, string? httpUri)
    {
        StreamingMessage message = new()
        {
            RequestId = requestId,
            WorkerInitResponse = new()
            {
                Capabilities = { ["WorkerIndexing"] = "true" },
                AppCapabilities = { ["application-capability"] = "application-value" },
                Result = new() { Status = StatusResult.Types.Status.Success, Result = "initialized" },
                WorkerMetadata = new() { RuntimeName = "dotnet-isolated", WorkerVersion = "test-version" }
            }
        };
        if (httpUri is not null)
        {
            message.WorkerInitResponse.Capabilities["HttpUri"] = httpUri;
        }

        return message;
    }

    private static IAsyncStreamReader<StreamingMessage> CreateMessageReader(ChannelReader<StreamingMessage> messages)
    {
        StreamingMessage current = new();
        Mock<IAsyncStreamReader<StreamingMessage>> reader = new(MockBehavior.Strict);
        reader.SetupGet(value => value.Current).Returns(() => current);
        reader.Setup(value => value.MoveNext(It.IsAny<CancellationToken>())).Returns(async (CancellationToken cancellationToken) =>
        {
            current = await messages.ReadAsync(cancellationToken);
            return true;
        });
        return reader.Object;
    }

    private static IServerStreamWriter<StreamingMessage> CreateMessageWriter(ChannelWriter<StreamingMessage> messages)
    {
        Mock<IServerStreamWriter<StreamingMessage>> writer = new(MockBehavior.Strict);
        writer.Setup(value => value.WriteAsync(It.IsAny<StreamingMessage>(), It.IsAny<CancellationToken>()))
            .Returns((StreamingMessage message, CancellationToken cancellationToken) => messages.WriteAsync(message, cancellationToken).AsTask());
        return writer.Object;
    }
}
