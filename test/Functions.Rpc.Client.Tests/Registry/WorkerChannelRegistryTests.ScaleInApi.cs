// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Host.Controllers;
using Azure.Functions.Host.HostState;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    [Fact]
    public async Task PostScaleIn_AbsoluteRetainedCount_DrainsOnlyExcessAndRepeatIsNoOp()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        ChannelControl first = CreateClassifiedWorker("worker-z");
        ChannelControl second = CreateClassifiedWorker("worker-a");
        await LinkAndReadyWorkersAsync(harness, registry, function, first, second);
        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);

        using HttpResponseMessage retained = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":2}""");
        using HttpResponseMessage reduced = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":1}""");
        using HttpResponseMessage repeated = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":1}""");

        Assert.Equal(0, await ReadNewlyDrainingWorkerCountAsync(retained));
        Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(reduced));
        Assert.Equal(0, await ReadNewlyDrainingWorkerCountAsync(repeated));
        await WaitUntilAsync(() => first.DisposeCount + second.DisposeCount == 1);
        Assert.Single(registry.GetInitializedChannels());
    }

    [Fact]
    public async Task PostScaleIn_ExactGroupAndClass_DrainsOnlyMatchingWorkers()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        ChannelControl firstMatch = CreateClassifiedWorker("first-match");
        ChannelControl secondMatch = CreateClassifiedWorker("second-match");
        ChannelControl wrongCaseGroup = CreateClassifiedWorker("wrong-group", functionGroup: "Durable");
        ChannelControl wrongClass = CreateClassifiedWorker("wrong-class", isAlwaysReady: true);
        ChannelControl httpWorker = new(
            "http-worker", functionGroupName: FunctionGroups.Http, isAlwaysReady: false, maxHttpConcurrency: 10);
        await LinkAndReadyWorkersAsync(
            harness,
            registry,
            function,
            firstMatch,
            secondMatch,
            wrongCaseGroup,
            wrongClass,
            httpWorker);

        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);
        using HttpResponseMessage response = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":0}""");

        Assert.Equal(2, await ReadNewlyDrainingWorkerCountAsync(response));
        await firstMatch.Transport.DisposeStarted.WaitAsync(TestTimeout);
        await secondMatch.Transport.DisposeStarted.WaitAsync(TestTimeout);
        Assert.Equal(0, wrongCaseGroup.DisposeCount);
        Assert.Equal(0, wrongClass.DisposeCount);
        Assert.Equal(0, httpWorker.DisposeCount);
        Assert.True(httpWorker.Channel.IsReadyForInvocations);
        Assert.Equal(10, httpWorker.Channel.MaxHttpConcurrency);
    }

    [Fact]
    public async Task PostScaleIn_StaleReadySnapshot_RoutesToRetainedGroupAndAccountsInvocation()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        function.Properties[RpcClientScriptHostLifecycleService.FunctionGroupKey] = FunctionGroups.Durable;
        ChannelControl first = CreateClassifiedWorker("first");
        ChannelControl second = CreateClassifiedWorker("second");
        ChannelControl unrelated = CreateHttpWorker("unrelated", false, 10);
        await LinkAndReadyWorkersAsync(harness, registry, function, first, second, unrelated);
        ScriptInvocationContext firstWork = CreateScaleInInvocation(function);
        ScriptInvocationContext secondWork = CreateScaleInInvocation(function);
        Assert.True(first.Channel.TryDispatchInvocation(firstWork));
        Assert.True(second.Channel.TryDispatchInvocation(secondWork));
        await first.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await second.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        IReadOnlyList<WorkerChannel> staleSnapshot = await registry.GetReadyChannelsAsync();
        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);
        using HttpResponseMessage response = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":1}""");
        Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(response));
        ChannelControl retained = first.Channel.IsReadyForInvocations ? first : second;
        ChannelControl draining = ReferenceEquals(retained, first) ? second : first;

        Mock<IWorkerChannelRegistry> staleRegistry = new();
        staleRegistry.Setup(current => current.GetReadyChannelsAsync()).Returns(new ValueTask<IReadOnlyList<WorkerChannel>>(staleSnapshot));
        staleRegistry.Setup(current => current.GetChangeTask()).Returns(registry.GetChangeTask);
        TaskCompletionSource accounted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.InvocationTracker.Setup(tracker => tracker.TryDispatchInvocation(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>()))
            .Returns((string _, string _, Func<bool> dispatch) => dispatch());
        harness.InvocationTracker.Setup(tracker => tracker.InvocationCompleted(It.IsAny<string>(), true))
            .Callback(() => accounted.TrySetResult());
        using RpcClientFunctionInvocationDispatcher dispatcher = new(
            staleRegistry.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RpcClientFunctionInvocationDispatcher>.Instance,
            harness.InvocationTracker.Object);
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext invocation = CreateScaleInInvocation(function);
        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await retained.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        string invocationId = invocation.ExecutionContext.InvocationId.ToString();
        Assert.Equal(invocationId, request.InvocationRequest.InvocationId);
        Assert.False(draining.Transport.Requests.TryRead(out _));
        Assert.False(unrelated.Transport.Requests.TryRead(out _));
        harness.InvocationTracker.Verify(
            tracker => tracker.TryDispatchInvocation(invocationId, retained.Id, It.IsAny<Func<bool>>()), Times.Once);
        await retained.Channel.InvokeResponse(new()
        {
            InvocationId = invocationId,
            Result = new() { Status = StatusResult.Types.Status.Success },
        });
        await accounted.Task.WaitAsync(TestTimeout);
        harness.InvocationTracker.Verify(tracker => tracker.InvocationCompleted(invocationId, true), Times.Once);
        Assert.False(draining.Transport.DisposeStarted.IsCompleted);
        await CompleteHttpWorkAsync(first, firstWork);
        await CompleteHttpWorkAsync(second, secondWork);
        await draining.Transport.DisposeStarted.WaitAsync(TestTimeout);
        Assert.True(retained.Channel.IsReadyForInvocations);
        Assert.Equal(0, unrelated.DisposeCount);
    }

    [Fact]
    public async Task PostScaleIn_ExplicitUnlinkDuringDrain_PreservesTerminalIdAndOtherWorkers()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        ChannelControl selected = CreateClassifiedWorker("selected");
        ChannelControl otherClass = CreateClassifiedWorker("other-class", isAlwaysReady: true);
        await LinkAndReadyWorkersAsync(harness, registry, function, selected, otherClass);
        ScriptInvocationContext invocation = CreateScaleInInvocation(function);
        Assert.True(selected.Channel.TryDispatchInvocation(invocation));
        await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);
        using HttpResponseMessage response = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":0}""");
        Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(response));
        Assert.False(selected.Transport.DisposeStarted.IsCompleted);

        Assert.True(await registry.UnlinkAsync(selected.Id));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => invocation.ResultSource.Task.WaitAsync(TestTimeout));
        WorkerLinkException terminated = await Assert.ThrowsAsync<WorkerLinkException>(() => LinkAsync(registry, selected.Id));
        Assert.Equal(WorkerLinkFailureReason.WorkerTerminated, terminated.Reason);
        Assert.Same(otherClass.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.True(otherClass.Channel.IsReadyForInvocations);
        Assert.Equal(0, otherClass.DisposeCount);
        Assert.Equal(1, selected.DisposeCount);
        Assert.Equal(2, harness.Transports.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostScaleIn_AcceptedWorkReachesTerminalResultBeforeLinkCloses(bool disconnectBeforeFunctionLoad)
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        function.Properties[RpcClientScriptHostLifecycleService.FunctionGroupKey] = FunctionGroups.Durable;
        ChannelControl selected = CreateClassifiedWorker("selected");
        harness.Enqueue(selected);
        await LinkAsync(registry, selected.Id);
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
        await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await WaitUntilAsync(() => selected.Channel.IsReadyForInvocations);

        using RpcClientFunctionInvocationDispatcher dispatcher = new(
            registry,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RpcClientFunctionInvocationDispatcher>.Instance);
        await dispatcher.InitializeAsync([function]);

        ScriptInvocationContext committed = CreateScaleInInvocation(function);
        await dispatcher.InvokeAsync(committed);

        StreamingMessage request = null;
        if (!disconnectBeforeFunctionLoad)
        {
            await selected.Transport.SendResponseAsync(new()
            {
                FunctionLoadResponse = new()
                {
                    FunctionId = function.GetFunctionId(),
                    Result = new() { Status = StatusResult.Types.Status.Success },
                },
            });
            request = await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        }

        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);
        using HttpResponseMessage response = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":0}""");

        Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(response));
        Assert.False(committed.ResultSource.Task.IsCompleted);
        Assert.False(selected.Transport.DisposeStarted.IsCompleted);
        Assert.False(selected.Channel.TryDispatchInvocation(CreateScaleInInvocation(function)));

        if (disconnectBeforeFunctionLoad)
        {
            IOException failure = new("Worker disconnected before function load completed.");
            selected.Complete(failure);
            IOException actual = await Assert.ThrowsAsync<IOException>(
                () => committed.ResultSource.Task.WaitAsync(TestTimeout));
            Assert.Same(failure, actual);
        }
        else
        {
            await selected.Transport.SendResponseAsync(new()
            {
                InvocationResponse = new()
                {
                    InvocationId = request.InvocationRequest.InvocationId,
                    Result = new()
                    {
                        Status = StatusResult.Types.Status.Failure,
                        Exception = new()
                        {
                            Message = "Invocation failed.",
                        },
                    },
                },
            });
            await Assert.ThrowsAsync<Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException>(
                () => committed.ResultSource.Task.WaitAsync(TestTimeout));
        }

        await selected.Transport.DisposeStarted.WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostScaleIn_SharedTrackingWaitsForAllAcceptedWorkAfterFailureOrCancellation(bool cancelFirst)
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        ChannelControl selected = CreateClassifiedWorker("selected");
        harness.Enqueue(selected);
        await LinkAsync(registry, selected.Id);
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
        await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await WaitUntilAsync(() => selected.Channel.IsReadyForInvocations);
        using CancellationTokenSource cancellation = new();
        ScriptInvocationContext first = CreateScaleInInvocation(function);
        first.CancellationToken = cancellation.Token;
        ScriptInvocationContext remaining = CreateScaleInInvocation(function);
        Assert.True(selected.Channel.TryDispatchInvocation(first));
        Assert.True(selected.Channel.TryPostInvocation(remaining));
        Task sharedDrain = selected.Channel.DrainInvocationsAsync();

        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);
        using HttpResponseMessage response = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":0}""");
        Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(response));
        if (cancelFirst)
        {
            cancellation.Cancel();
        }

        await selected.SendFunctionLoadResponseAsync(function.GetFunctionId());
        await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        if (cancelFirst)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => first.ResultSource.Task.WaitAsync(TestTimeout));
        }
        else
        {
            await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
            await selected.Channel.InvokeResponse(new()
            {
                InvocationId = first.ExecutionContext.InvocationId.ToString(),
                Result = new()
                {
                    Status = StatusResult.Types.Status.Failure,
                    Exception = new() { Message = "Invocation failed." },
                },
            });
            await Assert.ThrowsAsync<Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException>(
                () => first.ResultSource.Task.WaitAsync(TestTimeout));
        }

        Assert.False(sharedDrain.IsCompleted);
        Assert.False(remaining.ResultSource.Task.IsCompleted);
        Assert.False(selected.Transport.DisposeStarted.IsCompleted);
        Assert.False(selected.Channel.TryDispatchInvocation(CreateScaleInInvocation(function)));

        await selected.Channel.InvokeResponse(new()
        {
            InvocationId = remaining.ExecutionContext.InvocationId.ToString(),
            Result = new() { Status = StatusResult.Types.Status.Success },
        });
        Assert.NotNull(await remaining.ResultSource.Task.WaitAsync(TestTimeout));
        await sharedDrain.WaitAsync(TestTimeout);

        await selected.Transport.DisposeStarted.WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostScaleIn_DisposalUsesSharedShutdownCompletionAndTelemetry(bool functionLoaded)
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        ChannelControl selected = CreateClassifiedWorker("selected");
        harness.Enqueue(selected);
        await LinkAsync(registry, selected.Id);
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
        await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await WaitUntilAsync(() => selected.Channel.IsReadyForInvocations);
        if (functionLoaded)
        {
            await selected.SendFunctionLoadResponseAsync(function.GetFunctionId());
        }

        using Activity activity = new Activity("ScaleInInvocation").Start();
        ScriptInvocationContext invocation = CreateScaleInInvocation(function);
        Assert.True(selected.Channel.TryDispatchInvocation(invocation));
        if (functionLoaded)
        {
            await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        }

        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);
        using HttpResponseMessage response = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":0}""");
        Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(response));
        Assert.False(selected.Transport.DisposeStarted.IsCompleted);

        await selected.Channel.DisposeAsync();
        ObjectDisposedException failure = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => invocation.ResultSource.Task.WaitAsync(TestTimeout));
        await selected.Channel.DisposeAsync();

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(failure.Message, activity.StatusDescription);
        Assert.Single(activity.Events);
        Assert.False(selected.Channel.TryDispatchInvocation(CreateScaleInInvocation(function)));
        Assert.False(selected.Channel.IsExecutingInvocation(invocation.ExecutionContext.InvocationId.ToString()));
        Assert.Equal(1, selected.DisposeCount);
    }

    [Fact]
    public async Task PostScaleIn_HttpFunctionGroupIsRejected()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);

        using HttpResponseMessage response = await host.PostAsync(
            """{"functionGroup":"http","isAlwaysReady":false,"desiredWorkerCount":0}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement error = Assert.Single(body.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal("functionGroup", error.GetProperty("target").GetString());
    }

    private static ChannelControl CreateClassifiedWorker(
        string id,
        string functionGroup = FunctionGroups.Durable,
        bool isAlwaysReady = false)
        => new(id, functionGroupName: functionGroup, isAlwaysReady: isAlwaysReady);

    private static async Task LinkAndReadyWorkersAsync(
        RegistryHarness harness,
        WorkerChannelRegistry registry,
        FunctionMetadata function,
        params ChannelControl[] workers)
    {
        foreach (ChannelControl worker in workers)
        {
            harness.Enqueue(worker);
            await LinkAsync(registry, worker.Id);
        }

        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
        foreach (ChannelControl worker in workers)
        {
            await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
            await worker.Transport.SendResponseAsync(new()
            {
                FunctionLoadResponse = new()
                {
                    FunctionId = function.GetFunctionId(),
                    Result = new() { Status = StatusResult.Types.Status.Success },
                },
            });
        }

        await WaitUntilAsync(() => workers.All(worker => worker.Channel.IsReadyForInvocations));
    }

    private static ScriptInvocationContext CreateScaleInInvocation(FunctionMetadata function)
        => new()
        {
            FunctionMetadata = function,
            ExecutionContext = new()
            {
                FunctionName = function.Name,
                InvocationId = Guid.NewGuid(),
            },
            BindingData = [],
            Inputs = [],
            ResultSource = new(TaskCreationOptions.RunContinuationsAsynchronously),
            CancellationToken = CancellationToken.None,
            AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),
            Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
        };

    private static async Task<int> ReadNewlyDrainingWorkerCountAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("newlyDrainingWorkerCount").GetInt32();
    }

    private sealed class ScaleInApiHost : IAsyncDisposable
    {
        private readonly IHost _host;

        private ScaleInApiHost(IHost host)
        {
            _host = host;
            Client = host.GetTestClient();
        }

        internal HttpClient Client { get; }

        internal static async Task<ScaleInApiHost> StartAsync(
            IWorkerChannelRegistry registry, Action<IServiceCollection> configureServices = null)
        {
            IHost host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseSetting(WebHostDefaults.ApplicationKey, typeof(Startup).Assembly.GetName().Name)
                        .UseTestServer()
                        .ConfigureLogging(logging => logging.ClearProviders())
                        .ConfigureServices(services =>
                        {
                            IMvcBuilder mvcBuilder = services.AddMvc(options =>
                            {
                                options.EnableEndpointRouting = false;
                            }).AddNewtonsoftJson();
                            mvcBuilder.ConfigureApplicationPartManager(parts =>
                            {
                                parts.ApplicationParts.Clear();
                                parts.ApplicationParts.Add(new AssemblyPart(typeof(WorkerScaleInController).Assembly));
                            });

                            services.AddSingleton(registry);
                            services.AddSingleton(Mock.Of<IAppServerHostStatePublisher>());
                            configureServices?.Invoke(services);
                            services.AddAuthentication();
                            services.AddAuthorization(options =>
                            {
                                options.AddPolicy(
                                    PolicyNames.AdminAuthLevel,
                                    policy => policy.RequireAssertion(_ => true));
                            });
                        })
                        .Configure(app =>
                        {
                            app.UseAuthentication();
                            app.UseAuthorization();
                            app.UseMvc();
                        });
                })
                .Build();

            try
            {
                await host.StartAsync();
                return new(host);
            }
            catch
            {
                await DisposeHostAsync(host);
                throw;
            }
        }

        internal async Task<HttpResponseMessage> PostAsync(string json)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "/admin/workers/scale-in");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return await Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            using CancellationTokenSource timeout = new(TestTimeout);
            try
            {
                await _host.StopAsync(timeout.Token);
            }
            finally
            {
                await DisposeHostAsync(_host);
            }
        }

        private static async Task DisposeHostAsync(IHost host)
        {
            if (host is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            }
            else
            {
                host.Dispose();
            }
        }
    }
}
