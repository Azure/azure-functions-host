// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Host.HostState;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    private static readonly Uri HttpProxyEndpoint = new("http://worker-proxy:8080");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunScaleInPassAsync_AppliedFence_WaitsForHttpCompletionAndDrainsOneWorkerExactlyOnce(
        bool resultCompletesFirst)
    {
        ManualTimeProvider timeProvider = new();
        TaskCompletionSource forwarding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IHttpProxyService> proxy = new(MockBehavior.Strict);
        proxy.Setup(service => service.StartForwarding(It.IsAny<ScriptInvocationContext>(), HttpProxyEndpoint));
        proxy.Setup(service => service.EnsureSuccessfulForwardingAsync(It.IsAny<ScriptInvocationContext>()))
            .Returns(forwarding.Task);
        RegistryHarness harness = new(timeProvider);
        ChannelControl remainingWorker = new(
            "remaining-worker",
            functionGroupName: FunctionGroups.Http,
            isAlwaysReady: bool.TrueString,
            maxHttpConcurrency: "3",
            completeResponsesOnRequestCompletion: true);
        ChannelControl worker = CreateScaleInChannel(
            "worker",
            maxHttpConcurrency: "7",
            httpProxyService: proxy.Object,
            completeResponsesOnRequestCompletion: true,
            advertisedHttpUri: HttpProxyEndpoint.AbsoluteUri);
        harness.Enqueue(worker);
        harness.Enqueue(remainingWorker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateScaleInHttpFunction();
        await LinkAndLoadAsync(registry, function, worker, remainingWorker);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(registry, function);
        ScriptInvocationContext invocation = CreateScaleInInvocation(function);

        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        if (resultCompletesFirst)
        {
            await SendInvocationResponseAsync(worker, request.InvocationRequest.InvocationId);
        }
        else
        {
            forwarding.SetResult();
        }

        IHttpWorkerScaleInManager scaleInManager = registry;
        Assert.False(worker.Channel.TryReserveIdleScaleIn(TimeSpan.Zero, out _));

        if (resultCompletesFirst)
        {
            forwarding.SetResult();
        }
        else
        {
            await SendInvocationResponseAsync(worker, request.InvocationRequest.InvocationId);
        }

        Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
        timeProvider.Advance(HttpOnDemandWorkerScaleInService.IdleDuration);
        HostStateSequenceHandler handler = new(static _ => CreateAppliedResponse());
        using HttpClient httpClient = new(handler);
        HttpOnDemandWorkerScaleInService service = CreateScaleInService(
            scaleInManager,
            timeProvider,
            httpClient);

        await service.RunScaleInPassAsync(CancellationToken.None);
        await WaitUntilAsync(() => worker.DisposeCount == 1);

        Assert.Equal([10L, 3L], handler.PublishedStates.Select(state => state.HttpCapacity));
        Assert.Equal(1, scaleInManager.Current.LinkedWorkerCount);
        Assert.Equal(3, scaleInManager.Current.HttpCapacity);
        Assert.Same(remainingWorker.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Equal(1, worker.DisposeCount);
        Assert.Equal(0, remainingWorker.DisposeCount);
        proxy.Verify(service => service.StartForwarding(invocation, HttpProxyEndpoint), Times.Once);
        proxy.Verify(service => service.EnsureSuccessfulForwardingAsync(invocation), Times.Once);
        proxy.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.PreconditionFailed, false)]
    [InlineData(HttpStatusCode.Accepted, true)]
    public async Task RunScaleInPassAsync_UnappliedOrStaleFence_RestoresCapacityAndInvocationAdmission(
        HttpStatusCode projectedStatus,
        bool changeStateAfterProjectedPublication)
    {
        ManualTimeProvider timeProvider = new();
        RegistryHarness harness = new(timeProvider);
        ChannelControl worker = CreateScaleInChannel("worker", maxHttpConcurrency: "7");
        ChannelControl alternateWorker = CreateScaleInChannel(
            "alternate-worker",
            maxHttpConcurrency: "3",
            completeResponsesOnRequestCompletion: true);
        harness.Enqueue(worker);
        harness.Enqueue(alternateWorker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateScaleInHttpFunction();
        await LinkAndLoadAsync(registry, function, worker, alternateWorker);
        timeProvider.Advance(HttpOnDemandWorkerScaleInService.IdleDuration);
        HostStateSequenceHandler handler = new(requestIndex => requestIndex == 1
            ? changeStateAfterProjectedPublication
                ? CreateAppliedResponse()
                : new(projectedStatus)
            : CreateAppliedResponse());
        ChannelControl staleWorker = null;
        if (changeStateAfterProjectedPublication)
        {
            staleWorker = CreateScaleInChannel("stale-worker", maxHttpConcurrency: "5");
            harness.Enqueue(staleWorker);
            handler.BeforeResponseAsync = requestIndex => requestIndex == 1
                ? registry.LinkAsync(staleWorker.Id, CreateEndpoint(staleWorker.Id))
                : Task.CompletedTask;
        }

        using HttpClient httpClient = new(handler);
        IHttpWorkerScaleInManager scaleInManager = registry;
        HttpOnDemandWorkerScaleInService service = CreateScaleInService(
            scaleInManager,
            timeProvider,
            httpClient);

        await service.RunScaleInPassAsync(CancellationToken.None);

        Assert.Equal([10L, 3L, 10L], handler.PublishedStates.Select(state => state.HttpCapacity));
        Assert.Equal(10, scaleInManager.Current.HttpCapacity);
        Assert.Equal(changeStateAfterProjectedPublication ? 3 : 2, scaleInManager.Current.LinkedWorkerCount);
        Assert.Equal(0, worker.DisposeCount);
        if (projectedStatus is HttpStatusCode.PreconditionFailed && !changeStateAfterProjectedPublication)
        {
            await service.RunScaleInPassAsync(CancellationToken.None);
            await WaitUntilAsync(() => alternateWorker.DisposeCount == 1);
            Assert.Equal(
                [10L, 3L, 10L, 10L, 7L],
                handler.PublishedStates.Select(state => state.HttpCapacity));
            Assert.Equal(7, scaleInManager.Current.HttpCapacity);
            Assert.Equal(1, scaleInManager.Current.LinkedWorkerCount);
        }

        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(registry, function);
        ScriptInvocationContext invocation = CreateScaleInInvocation(function);
        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await SendInvocationResponseAsync(worker, request.InvocationRequest.InvocationId);
        Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task InvocationAdmissionAndIdleReservation_RaceHasOneSafeWinner()
    {
        ManualTimeProvider timeProvider = new();
        RegistryHarness harness = new(timeProvider);
        ChannelControl worker = CreateScaleInChannel("worker", maxHttpConcurrency: "7");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateScaleInHttpFunction();
        await LinkAndLoadAsync(registry, function, worker);
        timeProvider.Advance(HttpOnDemandWorkerScaleInService.IdleDuration);
        IHttpWorkerScaleInManager scaleInManager = registry;
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(registry, function);

        for (int attempt = 0; attempt < 16; attempt++)
        {
            ScriptInvocationContext invocation = CreateScaleInInvocation(function);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<HttpWorkerScaleInReservation> reservationTask = Task.Run(async () =>
            {
                await release.Task;
                return scaleInManager.TryReserveIdleWorker(TimeSpan.Zero);
            });
            Task dispatchTask = Task.Run(async () =>
            {
                await release.Task;
                await dispatcher.InvokeAsync(invocation);
            });

            release.SetResult();
            HttpWorkerScaleInReservation reservation = await reservationTask.WaitAsync(TestTimeout);
            if (reservation is not null)
            {
                Assert.False(dispatchTask.IsCompleted);
                Assert.True(scaleInManager.CancelReservation(reservation));
            }

            await dispatchTask.WaitAsync(TestTimeout);
            StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
            Assert.Null(scaleInManager.TryReserveIdleWorker(TimeSpan.Zero));
            await SendInvocationResponseAsync(worker, request.InvocationRequest.InvocationId);
            Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));

            HttpWorkerScaleInReservation postInvocationReservation =
                Assert.IsType<HttpWorkerScaleInReservation>(scaleInManager.TryReserveIdleWorker(TimeSpan.Zero));
            Assert.True(scaleInManager.CancelReservation(postInvocationReservation));
        }
    }

    [Theory]
    [InlineData(FunctionGroups.Http, "true", "7", 7, "success")]
    [InlineData("non-http", "false", "7", 0, "success")]
    [InlineData(null, "false", "7", 0, "success")]
    [InlineData(FunctionGroups.Http, null, "7", 7, "success")]
    [InlineData(FunctionGroups.Http, "invalid", "7", 7, "success")]
    [InlineData(FunctionGroups.Http, "false", null, 0, "success")]
    [InlineData(FunctionGroups.Http, "false", "invalid", 0, "success")]
    [InlineData(FunctionGroups.Http, "false", "0", 0, "success")]
    [InlineData(FunctionGroups.Http, "false", "7", 0, "pending")]
    [InlineData(FunctionGroups.Http, "false", "7", 0, "failed")]
    public async Task IdleScaleIn_IneligibleOrUnreadyWorkerIsRetained(
        string functionGroupName,
        string isAlwaysReady,
        string maxHttpConcurrency,
        long expectedCapacity,
        string loadResponse)
    {
        ManualTimeProvider timeProvider = new();
        RegistryHarness harness = new(timeProvider);
        ChannelControl worker = new(
            "worker",
            functionGroupName: functionGroupName,
            isAlwaysReady: isAlwaysReady,
            maxHttpConcurrency: maxHttpConcurrency);
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateScaleInHttpFunction();
        await LinkAsync(registry, worker.Id);
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        if (!string.Equals(loadResponse, "pending", StringComparison.Ordinal))
        {
            await SendFunctionLoadResponseAsync(
                worker,
                function.GetFunctionId(),
                succeeded: !string.Equals(loadResponse, "failed", StringComparison.Ordinal));
            await WaitUntilAsync(() => worker.Channel.ScaleInFunctionLoadsComplete);
        }

        timeProvider.Advance(HttpOnDemandWorkerScaleInService.IdleDuration);
        IHttpWorkerScaleInManager scaleInManager = registry;

        Assert.Equal(expectedCapacity, scaleInManager.Current.HttpCapacity);
        Assert.Null(scaleInManager.TryReserveIdleWorker(HttpOnDemandWorkerScaleInService.IdleDuration));
        Assert.Equal(1, scaleInManager.Current.LinkedWorkerCount);
        if (string.Equals(loadResponse, "pending", StringComparison.Ordinal))
        {
            Assert.Equal(0, worker.DisposeCount);
            return;
        }

        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(registry, function);
        ScriptInvocationContext invocation = CreateScaleInInvocation(function);
        await dispatcher.InvokeAsync(invocation);
        if (string.Equals(loadResponse, "failed", StringComparison.Ordinal))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => invocation.ResultSource.Task.WaitAsync(TestTimeout));
        }
        else
        {
            StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
            await SendInvocationResponseAsync(worker, request.InvocationRequest.InvocationId);
            Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
        }

        Assert.Equal(0, worker.DisposeCount);
    }

    private static ChannelControl CreateScaleInChannel(
        string workerId,
        string maxHttpConcurrency,
        IHttpProxyService httpProxyService = null,
        bool completeResponsesOnRequestCompletion = false,
        string advertisedHttpUri = null)
        => new(
            workerId,
            functionGroupName: FunctionGroups.Http,
            isAlwaysReady: bool.FalseString,
            maxHttpConcurrency: maxHttpConcurrency,
            httpProxyService: httpProxyService,
            completeResponsesOnRequestCompletion: completeResponsesOnRequestCompletion,
            advertisedHttpUri: advertisedHttpUri);

    private static RpcClientFunctionInvocationDispatcher CreateDispatcher(
        WorkerChannelRegistry registry,
        FunctionMetadata function)
    {
        RpcClientFunctionInvocationDispatcher dispatcher = new(
            registry,
            NullLogger<RpcClientFunctionInvocationDispatcher>.Instance);
        dispatcher.InitializeAsync([function]).GetAwaiter().GetResult();
        return dispatcher;
    }

    private static async Task LinkAndLoadAsync(
        WorkerChannelRegistry registry,
        FunctionMetadata function,
        params ChannelControl[] workers)
    {
        foreach (ChannelControl worker in workers)
        {
            await LinkAsync(registry, worker.Id);
        }

        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
        foreach (ChannelControl worker in workers)
        {
            await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
            await SendFunctionLoadResponseAsync(worker, function.GetFunctionId());
        }

        foreach (ChannelControl worker in workers)
        {
            await WaitUntilAsync(() => worker.Channel.ScaleInFunctionLoadsComplete);
        }
    }

    private static FunctionMetadata CreateScaleInHttpFunction()
    {
        FunctionMetadata function = CreateFunction("HttpFunction");
        function.Bindings.Add(new()
        {
            Name = "req",
            Type = "httpTrigger",
            Direction = BindingDirection.In,
        });

        return function;
    }

    private static ScriptInvocationContext CreateScaleInInvocation(FunctionMetadata function)
    {
        HttpRequest request = new DefaultHttpContext().Request;
        request.Method = "POST";
        request.Host = new("functions.test");
        request.Path = "/api/test";
        request.ContentType = "application/json";
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        request.ContentLength = request.Body.Length;

        return new()
        {
            FunctionMetadata = function,
            ExecutionContext = new()
            {
                FunctionName = function.Name,
                InvocationId = Guid.NewGuid(),
            },
            BindingData = [],
            Inputs = [("req", DataType.String, request)],
            ResultSource = new(TaskCreationOptions.RunContinuationsAsynchronously),
            AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),
            Logger = NullLogger.Instance,
        };
    }

    private static ValueTask SendInvocationResponseAsync(ChannelControl worker, string invocationId)
        => worker.Transport.SendResponseAsync(new()
        {
            InvocationResponse = new()
            {
                InvocationId = invocationId,
                Result = new() { Status = StatusResult.Types.Status.Success },
            },
        });

    private static ValueTask SendFunctionLoadResponseAsync(
        ChannelControl worker,
        string functionId,
        bool succeeded = true)
        => worker.Transport.SendResponseAsync(new()
        {
            FunctionLoadResponse = new()
            {
                FunctionId = functionId,
                Result = new()
                {
                    Status = succeeded ? StatusResult.Types.Status.Success : StatusResult.Types.Status.Failure,
                    Exception = succeeded ? null : new() { Message = "function load failed" },
                },
            },
        });

    private static HttpOnDemandWorkerScaleInService CreateScaleInService(
        IHttpWorkerScaleInManager scaleInManager,
        TimeProvider timeProvider,
        HttpClient httpClient)
    {
        Mock<IHttpClientFactory> httpClientFactory = new();
        httpClientFactory
            .Setup(factory => factory.CreateClient(AppServerHostStateClient.HttpClientName))
            .Returns(httpClient);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                [EnvironmentSettingNames.ContainerName] = "test-container",
                [EnvironmentSettingNames.MeshInitURI] = "http://appserver.test/",
            })
            .Build();
        AppServerHostStateClient client = new(httpClientFactory.Object, configuration);

        return new(
            scaleInManager,
            client,
            timeProvider,
            Mock.Of<IHostApplicationLifetime>(),
            NullLogger<HttpOnDemandWorkerScaleInService>.Instance);
    }

    private static HttpResponseMessage CreateAppliedResponse()
    {
        HttpResponseMessage response = new(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation(
            AppServerHostStateClient.CapacityFenceHeader,
            AppServerHostStateClient.CapacityFenceApplied);
        return response;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
            _utcNow += duration;
            _timestamp += duration.Ticks;
        }
    }

    private sealed class HostStateSequenceHandler(
        Func<int, HttpResponseMessage> createResponse) : HttpMessageHandler
    {
        private int _requestCount;

        public Func<int, Task> BeforeResponseAsync { get; set; }

        public List<AppServerHostStateClient.HostStateRequest> PublishedStates { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal(
                AppServerHostStateClient.CapacityFenceRequired,
                Assert.Single(request.Headers.GetValues(AppServerHostStateClient.CapacityFenceHeader)));
            string json = await request.Content.ReadAsStringAsync(cancellationToken);
            AppServerHostStateClient.HostStateRequest state = Assert.IsType<AppServerHostStateClient.HostStateRequest>(
                JsonSerializer.Deserialize(json, AppServerHostStateJsonContext.Default.HostStateRequest));
            PublishedStates.Add(state);

            int requestIndex = _requestCount++;
            if (BeforeResponseAsync is not null)
            {
                await BeforeResponseAsync(requestIndex);
            }

            return createResponse(requestIndex);
        }
    }
}
