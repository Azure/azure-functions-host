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
    public async Task ExecuteAsync_TenMinuteCadenceSelectsLowestOrdinalWorkerAndWaitsForHttpCompletion(
        bool resultCompletesFirst)
    {
        ManualTimeProvider timeProvider = new();
        TaskCompletionSource forwarding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IHttpProxyService> proxy = new(MockBehavior.Strict);
        proxy.Setup(service => service.StartForwarding(It.IsAny<ScriptInvocationContext>(), HttpProxyEndpoint));
        proxy.Setup(service => service.EnsureSuccessfulForwardingAsync(It.IsAny<ScriptInvocationContext>()))
            .Returns(forwarding.Task);
        RegistryHarness harness = new(timeProvider);
        ChannelControl laterWorker = CreateScaleInChannel(
            "worker-z",
            maxHttpConcurrency: "1",
            completeResponsesOnRequestCompletion: true);
        ChannelControl worker = CreateScaleInChannel(
            "worker-a",
            maxHttpConcurrency: "7",
            httpProxyService: proxy.Object,
            blockDisposal: true,
            completeResponsesOnRequestCompletion: true,
            advertisedHttpUri: HttpProxyEndpoint.AbsoluteUri);
        ChannelControl remainingWorker = new(
            "remaining-worker",
            functionGroupName: FunctionGroups.Http,
            isAlwaysReady: bool.TrueString,
            maxHttpConcurrency: "3",
            completeResponsesOnRequestCompletion: true);
        harness.Enqueue(laterWorker);
        harness.Enqueue(worker);
        harness.Enqueue(remainingWorker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateScaleInHttpFunction();
        await LinkAndLoadAsync(registry, function, laterWorker, worker, remainingWorker);
        laterWorker.Channel.SetScaleInReady(false);
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

        Assert.False(worker.Channel.TryReserveScaleIn(out _));

        if (resultCompletesFirst)
        {
            forwarding.SetResult();
        }
        else
        {
            await SendInvocationResponseAsync(worker, request.InvocationRequest.InvocationId);
        }

        Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
        IHttpWorkerScaleInManager scaleInManager = registry;
        HttpWorkerScaleInReservation completedInvocationReservation = null;
        await WaitUntilAsync(() =>
        {
            completedInvocationReservation = scaleInManager.TryReserveWorker();
            return completedInvocationReservation is not null;
        });
        Assert.True(scaleInManager.CancelReservation(
            completedInvocationReservation,
            out bool invocationAdmissionRestored));
        Assert.True(invocationAdmissionRestored);
        laterWorker.Channel.SetScaleInReady(true);
        HostStateSequenceHandler handler = new(static _ => CreateAppliedResponse());
        using HttpClient httpClient = new(handler);
        HttpOnDemandWorkerScaleInService service = CreateScaleInService(
            registry,
            timeProvider,
            httpClient);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => timeProvider.NextTimerDueTime.HasValue);
            Assert.Equal(
                HttpOnDemandWorkerScaleInService.AttemptInterval,
                timeProvider.NextTimerDueTime);
            Assert.Empty(handler.PublishedStates);

            timeProvider.FireNextTimer();
            await worker.DisposeStarted.WaitAsync(TestTimeout);

            IHostStateCoordinator hostStateCoordinator = registry;
            Assert.Equal(3, hostStateCoordinator.Current.LinkedWorkerCount);
            Assert.Equal(4, hostStateCoordinator.Current.HttpCapacity);
            Assert.Equal([11L, 4L], handler.PublishedStates.Select(state => state.HttpCapacity));

            worker.AllowDispose();
            await WaitUntilAsync(() => hostStateCoordinator.Current.LinkedWorkerCount == 2);

            await WaitUntilAsync(() =>
                timeProvider.NextTimerDueTime == HttpOnDemandWorkerScaleInService.AttemptInterval);
            timeProvider.FireNextTimer();
            await WaitUntilAsync(() => laterWorker.DisposeCount == 1);

            Assert.Equal(
                [11L, 4L, 4L, 3L],
                handler.PublishedStates.Select(state => state.HttpCapacity));
            Assert.Equal(1, hostStateCoordinator.Current.LinkedWorkerCount);
            Assert.Equal(3, hostStateCoordinator.Current.HttpCapacity);
            Assert.Same(remainingWorker.Channel, Assert.Single(registry.GetInitializedChannels()));
        }
        finally
        {
            worker.AllowDispose();
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Equal(1, worker.DisposeCount);
        Assert.Equal(1, laterWorker.DisposeCount);
        Assert.Equal(0, remainingWorker.DisposeCount);
        proxy.Verify(service => service.StartForwarding(invocation, HttpProxyEndpoint), Times.Once);
        proxy.Verify(service => service.EnsureSuccessfulForwardingAsync(invocation), Times.Once);
        proxy.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, false, false)]
    [InlineData(HttpStatusCode.PreconditionFailed, false, false)]
    [InlineData(HttpStatusCode.Accepted, true, false)]
    [InlineData(HttpStatusCode.OK, false, true)]
    public async Task RunScaleInPassAsync_UnappliedOrStaleFence_RestoresCapacityAndInvocationAdmission(
        HttpStatusCode projectedStatus,
        bool changeStateAfterProjectedPublication,
        bool closeRuntimeAdmissionAfterProjectedPublication)
    {
        ManualTimeProvider timeProvider = new();
        RegistryHarness harness = new(timeProvider);
        ChannelControl worker = CreateScaleInChannel(
            "a-worker",
            maxHttpConcurrency: "7",
            completeResponsesOnRequestCompletion: true);
        ChannelControl alternateWorker = CreateScaleInChannel(
            "z-worker",
            maxHttpConcurrency: "3",
            completeResponsesOnRequestCompletion: true);
        harness.Enqueue(worker);
        harness.Enqueue(alternateWorker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateScaleInHttpFunction();
        await LinkAndLoadAsync(registry, function, worker, alternateWorker);
        ChannelControl pendingWorker = null;
        Task<WorkerLinkResult> pendingLink = null;
        if (closeRuntimeAdmissionAfterProjectedPublication)
        {
            pendingWorker = new(
                "pending-worker",
                blockStart: true,
                blockDisposal: true,
                functionGroupName: FunctionGroups.Http,
                isAlwaysReady: bool.FalseString,
                maxHttpConcurrency: "5");
            harness.Enqueue(pendingWorker);
            pendingLink = registry.LinkAsync(
                pendingWorker.Id,
                CreateEndpoint(pendingWorker.Id));
            await pendingWorker.StartEntered.WaitAsync(TestTimeout);
        }

        HostStateSequenceHandler handler = new(requestIndex => requestIndex == 1
            ? changeStateAfterProjectedPublication ||
                closeRuntimeAdmissionAfterProjectedPublication
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
        else if (closeRuntimeAdmissionAfterProjectedPublication)
        {
            IRuntimeWorkerAdmissionGate runtimeAdmissionGate = registry;
            handler.BeforeResponseAsync = requestIndex =>
            {
                if (requestIndex == 1)
                {
                    Assert.True(runtimeAdmissionGate.TryClose());
                }

                return Task.CompletedTask;
            };
        }

        using HttpClient httpClient = new(handler);
        HttpOnDemandWorkerScaleInService service = CreateScaleInService(
            registry,
            timeProvider,
            httpClient);

        await service.RunScaleInPassAsync(CancellationToken.None);

        IHostStateCoordinator hostStateCoordinator = registry;
        if (closeRuntimeAdmissionAfterProjectedPublication)
        {
            Assert.Equal([10L, 3L], handler.PublishedStates.Select(state => state.HttpCapacity));
            Assert.Equal(
                [3, 3],
                handler.PublishedStates.Select(state => state.LinkedWorkerCount));
            Assert.Equal(0, hostStateCoordinator.Current.HttpCapacity);
            Assert.Equal(3, hostStateCoordinator.Current.LinkedWorkerCount);
            Assert.Equal(0, worker.DisposeCount);
            Assert.Equal(0, alternateWorker.DisposeCount);
            Assert.Same(
                alternateWorker.Channel,
                Assert.Single(await registry.GetReadyChannelsAsync()));

            pendingWorker.AllowStart();
            await pendingWorker.DisposeStarted.WaitAsync(TestTimeout);
            Assert.Equal(3, hostStateCoordinator.Current.LinkedWorkerCount);
            long disposingVersion = hostStateCoordinator.Version;
            Task<long> physicalUnlinkChange =
                hostStateCoordinator.WaitForChangeAsync(disposingVersion);
            pendingWorker.AllowDispose();
            WorkerLinkException rejection = await Assert.ThrowsAsync<WorkerLinkException>(
                () => pendingLink.WaitAsync(TestTimeout));
            Assert.Equal(WorkerLinkFailureReason.RuntimeStopping, rejection.Reason);
            Assert.True(await physicalUnlinkChange.WaitAsync(TestTimeout) > disposingVersion);
            await WaitUntilAsync(() => hostStateCoordinator.Current.LinkedWorkerCount == 2);

            HostStateSequenceHandler authoritativeHandler = new(
                static _ => new(HttpStatusCode.OK));
            using HttpClient authoritativeHttpClient = new(authoritativeHandler);
            HostStatePublicationCoordinator publicationCoordinator =
                CreatePublicationCoordinator(registry, authoritativeHttpClient);
            AppServerHostStatePublishResult authoritativeResult =
                await publicationCoordinator.PublishAuthoritativeAsync(
                    hostStateCoordinator.Current,
                    CancellationToken.None);
            Assert.Equal(
                AppServerHostStatePublishResult.Applied,
                authoritativeResult);
            Assert.Equal(
                [AppServerHostStatePublishMode.Authoritative],
                authoritativeHandler.PublishModes);
            Assert.Equal(
                [0L],
                authoritativeHandler.PublishedStates.Select(state => state.HttpCapacity));
            return;
        }

        Assert.Equal([10L, 3L, 10L], handler.PublishedStates.Select(state => state.HttpCapacity));
        Assert.Equal(10, hostStateCoordinator.Current.HttpCapacity);
        Assert.Equal(
            changeStateAfterProjectedPublication ? 3 : 2,
            hostStateCoordinator.Current.LinkedWorkerCount);
        Assert.Equal(0, worker.DisposeCount);
        if (projectedStatus is HttpStatusCode.PreconditionFailed && !changeStateAfterProjectedPublication)
        {
            await service.RunScaleInPassAsync(CancellationToken.None);
            await WaitUntilAsync(() => worker.DisposeCount == 1);
            Assert.Equal(
                [10L, 3L, 10L, 10L, 3L],
                handler.PublishedStates.Select(state => state.HttpCapacity));
            Assert.Equal(3, hostStateCoordinator.Current.HttpCapacity);
            Assert.Equal(1, hostStateCoordinator.Current.LinkedWorkerCount);
            Assert.Equal(0, alternateWorker.DisposeCount);
            return;
        }

        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(registry, function);
        ScriptInvocationContext invocation = CreateScaleInInvocation(function);
        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await SendInvocationResponseAsync(worker, request.InvocationRequest.InvocationId);
        Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task InvocationAdmissionAndScaleInReservation_RaceHasOneSafeWinner()
    {
        ManualTimeProvider timeProvider = new();
        RegistryHarness harness = new(timeProvider);
        ChannelControl worker = CreateScaleInChannel("worker", maxHttpConcurrency: "7");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateScaleInHttpFunction();
        await LinkAndLoadAsync(registry, function, worker);
        IHttpWorkerScaleInManager scaleInManager = registry;
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(registry, function);

        for (int attempt = 0; attempt < 16; attempt++)
        {
            ScriptInvocationContext invocation = CreateScaleInInvocation(function);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<HttpWorkerScaleInReservation> reservationTask = Task.Run(async () =>
            {
                await release.Task;
                return scaleInManager.TryReserveWorker();
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
                Assert.True(scaleInManager.CancelReservation(
                    reservation,
                    out bool invocationAdmissionRestored));
                Assert.True(invocationAdmissionRestored);
            }

            await dispatchTask.WaitAsync(TestTimeout);
            StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
            Assert.Null(scaleInManager.TryReserveWorker());
            await SendInvocationResponseAsync(worker, request.InvocationRequest.InvocationId);
            Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));

            HttpWorkerScaleInReservation postInvocationReservation =
                Assert.IsType<HttpWorkerScaleInReservation>(scaleInManager.TryReserveWorker());
            Assert.True(scaleInManager.CancelReservation(
                postInvocationReservation,
                out bool postInvocationAdmissionRestored));
            Assert.True(postInvocationAdmissionRestored);
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
    [InlineData(FunctionGroups.Http, "false", "7", 7, "busy")]
    [InlineData(FunctionGroups.Http, "false", "7", 0, "legacy")]
    public async Task ScaleIn_IneligibleUnreadyOrBusyWorkerIsRetained(
        string functionGroupName,
        string isAlwaysReady,
        string maxHttpConcurrency,
        long expectedCapacity,
        string loadResponse)
    {
        ManualTimeProvider timeProvider = new();
        RegistryHarness harness = new(timeProvider);
        bool trustedAssignmentMetadata =
            !string.Equals(loadResponse, "legacy", StringComparison.Ordinal);
        ChannelControl worker = new(
            "worker",
            functionGroupName: functionGroupName,
            isAlwaysReady: isAlwaysReady,
            maxHttpConcurrency: maxHttpConcurrency,
            trustedAssignmentMetadata: trustedAssignmentMetadata);
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateScaleInHttpFunction();
        await LinkAsync(registry, worker.Id);
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        IHttpWorkerScaleInManager scaleInManager = registry;
        IHostStateCoordinator hostStateCoordinator = registry;
        long pendingCapacityVersion = 0;
        Task<long> capacityChange = null;
        if (expectedCapacity > 0 &&
            string.Equals(loadResponse, "success", StringComparison.Ordinal))
        {
            pendingCapacityVersion = hostStateCoordinator.Version;
            capacityChange = hostStateCoordinator.WaitForChangeAsync(pendingCapacityVersion);
        }

        if (!string.Equals(loadResponse, "pending", StringComparison.Ordinal))
        {
            await SendFunctionLoadResponseAsync(
                worker,
                function.GetFunctionId(),
                succeeded: !string.Equals(loadResponse, "failed", StringComparison.Ordinal));
            await WaitUntilAsync(() => worker.Channel.ScaleInFunctionLoadsComplete);
        }

        if (capacityChange is not null)
        {
            Assert.True(await capacityChange.WaitAsync(TestTimeout) > pendingCapacityVersion);
        }

        if (string.Equals(loadResponse, "busy", StringComparison.Ordinal))
        {
            using RpcClientFunctionInvocationDispatcher busyDispatcher = CreateDispatcher(registry, function);
            ScriptInvocationContext busyInvocation = CreateScaleInInvocation(function);
            await busyDispatcher.InvokeAsync(busyInvocation);
            StreamingMessage busyRequest = await worker.ReadRequestAsync(
                StreamingMessage.ContentOneofCase.InvocationRequest);

            Assert.Equal(expectedCapacity, hostStateCoordinator.Current.HttpCapacity);
            Assert.Null(scaleInManager.TryReserveWorker());
            Assert.Equal(1, hostStateCoordinator.Current.LinkedWorkerCount);

            await SendInvocationResponseAsync(worker, busyRequest.InvocationRequest.InvocationId);
            Assert.NotNull(await busyInvocation.ResultSource.Task.WaitAsync(TestTimeout));
            Assert.Equal(0, worker.DisposeCount);
            return;
        }

        Assert.Equal(expectedCapacity, hostStateCoordinator.Current.HttpCapacity);
        Assert.Null(scaleInManager.TryReserveWorker());
        Assert.Equal(1, hostStateCoordinator.Current.LinkedWorkerCount);
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
        bool blockDisposal = false,
        bool completeResponsesOnRequestCompletion = false,
        string advertisedHttpUri = null)
        => new(
            workerId,
            blockDisposal: blockDisposal,
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
        WorkerChannelRegistry registry,
        TimeProvider timeProvider,
        HttpClient httpClient)
        => new(
            registry,
            registry,
            CreatePublicationCoordinator(registry, httpClient),
            timeProvider,
            Mock.Of<IHostApplicationLifetime>(),
            NullLogger<HttpOnDemandWorkerScaleInService>.Instance);

    private static HostStatePublicationCoordinator CreatePublicationCoordinator(
        WorkerChannelRegistry registry,
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

        return new(registry, httpClientFactory.Object, configuration);
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
        private readonly Lock _lock = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public TimeSpan? NextTimerDueTime
        {
            get
            {
                lock (_lock)
                {
                    long? dueTimestamp = _timers
                        .Select(static timer => timer.DueTimestamp)
                        .Where(static timestamp => timestamp.HasValue)
                        .Min();
                    return dueTimestamp is { } due
                        ? TimeSpan.FromTicks(due - _timestamp)
                        : null;
                }
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock)
            {
                return _utcNow;
            }
        }

        public override long GetTimestamp()
        {
            lock (_lock)
            {
                return _timestamp;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            lock (_lock)
            {
                ManualTimer timer = new(this, callback, state);
                timer.ChangeLocked(dueTime, period);
                _timers.Add(timer);
                return timer;
            }
        }

        public void FireNextTimer()
        {
            List<(TimerCallback Callback, object State)> callbacks = [];
            lock (_lock)
            {
                ManualTimer timer = _timers
                    .Where(static timer => timer.DueTimestamp.HasValue)
                    .MinBy(static timer => timer.DueTimestamp.Value)
                    ?? throw new InvalidOperationException("No timer is scheduled.");
                long dueTimestamp = timer.DueTimestamp.Value;
                TimeSpan duration = TimeSpan.FromTicks(dueTimestamp - _timestamp);
                _utcNow += duration;
                _timestamp = dueTimestamp;
                timer.CollectCallbackLocked(_timestamp, callbacks);
            }

            foreach ((TimerCallback callback, object state) in callbacks)
            {
                callback(state);
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object state) : ITimer
        {
            private readonly TimerCallback _callback = callback;
            private readonly ManualTimeProvider _owner = owner;
            private readonly object _state = state;
            private bool _disposed;
            private long? _dueTimestamp;
            private TimeSpan _period;

            internal long? DueTimestamp => _dueTimestamp;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_owner._lock)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    ChangeLocked(dueTime, period);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (_owner._lock)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    _dueTimestamp = null;
                    _owner._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal void ChangeLocked(TimeSpan dueTime, TimeSpan period)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, Timeout.InfiniteTimeSpan);
                ArgumentOutOfRangeException.ThrowIfLessThan(period, Timeout.InfiniteTimeSpan);
                _dueTimestamp = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : checked(_owner._timestamp + dueTime.Ticks);
                _period = period;
            }

            internal void CollectCallbackLocked(
                long timestamp,
                List<(TimerCallback Callback, object State)> callbacks)
            {
                if (_disposed ||
                    _dueTimestamp is not { } dueTimestamp ||
                    timestamp < dueTimestamp)
                {
                    return;
                }

                callbacks.Add((_callback, _state));
                _dueTimestamp = _period is { Ticks: > 0 }
                    ? checked(timestamp + _period.Ticks)
                    : null;
            }
        }
    }

    private sealed class HostStateSequenceHandler(
        Func<int, HttpResponseMessage> createResponse) : HttpMessageHandler
    {
        private int _requestCount;

        public Func<int, Task> BeforeResponseAsync { get; set; }

        public List<AppServerHostStatePublishMode> PublishModes { get; } = [];

        public List<AppServerHostStateClient.HostStateRequest> PublishedStates { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            AppServerHostStatePublishMode mode;
            if (request.Headers.TryGetValues(
                AppServerHostStateClient.CapacityFenceHeader,
                out IEnumerable<string> values))
            {
                Assert.Equal(
                    AppServerHostStateClient.CapacityFenceRequired,
                    Assert.Single(values));
                mode = AppServerHostStatePublishMode.CapacityFence;
            }
            else
            {
                mode = AppServerHostStatePublishMode.Authoritative;
            }

            PublishModes.Add(mode);
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
