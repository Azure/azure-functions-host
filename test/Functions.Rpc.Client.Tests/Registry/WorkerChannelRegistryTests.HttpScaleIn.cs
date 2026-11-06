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
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.Host.HostState;
using Azure.Functions.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task PostScaleIn_HttpAlwaysReady_UsesSharedDrainAndPreservesOnDemand(int retained)
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateHttpFunction();
        ChannelControl first = CreateHttpWorker("always-first", true, 7);
        ChannelControl second = CreateHttpWorker("always-second", true, 13);
        ChannelControl onDemand = CreateHttpWorker("on-demand", false, 17);
        await LinkAndReadyWorkersAsync(harness, registry, function, first, second, onDemand);
        ScriptInvocationContext firstWork = CreateScaleInInvocation(function);
        ScriptInvocationContext secondWork = CreateScaleInInvocation(function);
        Assert.True(first.Channel.TryDispatchInvocation(firstWork));
        Assert.True(second.Channel.TryDispatchInvocation(secondWork));
        await first.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await second.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);
        using HttpResponseMessage onDemandRejection = await host.PostAsync(
            """{"functionGroup":"http","isAlwaysReady":false,"desiredWorkerCount":0}""");
        Assert.Equal(HttpStatusCode.BadRequest, onDemandRejection.StatusCode);
        Assert.True(onDemand.Channel.IsReadyForInvocations);
        string body = $$"""{"functionGroup":"HtTp","isAlwaysReady":true,"desiredWorkerCount":{{retained}}}""";
        using HttpResponseMessage response = await host.PostAsync(body);
        using HttpResponseMessage repeated = await host.PostAsync(body);
        Assert.Equal(2 - retained, await ReadNewlyDrainingWorkerCountAsync(response));
        Assert.Equal(0, await ReadNewlyDrainingWorkerCountAsync(repeated));
        Assert.Equal(retained, new[] { first, second }.Count(worker => worker.Channel.IsReadyForInvocations));
        Assert.Equal(0, first.DisposeCount + second.DisposeCount);
        Assert.True(onDemand.Channel.IsReadyForInvocations);
        Assert.Equal(0, onDemand.DisposeCount);
        await CompleteHttpWorkAsync(first, firstWork);
        await CompleteHttpWorkAsync(second, secondWork);
        await WaitUntilAsync(() => first.DisposeCount + second.DisposeCount == 2 - retained);
        using HttpResponseMessage afterRemoval = await host.PostAsync(body);
        Assert.Equal(0, await ReadNewlyDrainingWorkerCountAsync(afterRemoval));
    }

    [Fact]
    public async Task PostScaleIn_ConcurrentHttpTargets_SelectEachWorkerOnlyOnce()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateHttpFunction();
        ChannelControl first = CreateHttpWorker("first", true, 10);
        ChannelControl second = CreateHttpWorker("second", true, 10);
        await LinkAndReadyWorkersAsync(harness, registry, function, first, second);
        ScriptInvocationContext firstWork = CreateScaleInInvocation(function);
        ScriptInvocationContext secondWork = CreateScaleInInvocation(function);
        Assert.True(first.Channel.TryDispatchInvocation(firstWork));
        Assert.True(second.Channel.TryDispatchInvocation(secondWork));
        await first.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await second.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);
        Task<HttpResponseMessage> retainOne = host.PostAsync(
            """{"functionGroup":"http","isAlwaysReady":true,"desiredWorkerCount":1}""");
        Task<HttpResponseMessage> retainNone = host.PostAsync(
            """{"functionGroup":"http","isAlwaysReady":true,"desiredWorkerCount":0}""");
        using HttpResponseMessage one = await retainOne.WaitAsync(TestTimeout);
        using HttpResponseMessage none = await retainNone.WaitAsync(TestTimeout);
        Assert.Equal(2, (await ReadNewlyDrainingWorkerCountAsync(one)) + await ReadNewlyDrainingWorkerCountAsync(none));
        Assert.False(first.Channel.IsReadyForInvocations);
        Assert.False(second.Channel.IsReadyForInvocations);
        Assert.Equal(0, first.DisposeCount + second.DisposeCount);
        await CompleteHttpWorkAsync(first, firstWork);
        await CompleteHttpWorkAsync(second, secondWork);
        await first.Transport.DisposeStarted.WaitAsync(TestTimeout);
        await second.Transport.DisposeStarted.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task PostScaleIn_HttpAlwaysReady_ActualHttpResponseCompletesBeforeRemoval()
    {
        TaskCompletionSource responseStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using TestCertificateFiles certificates = new();
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener =>
        {
            listener.Protocols = HttpProtocols.Http1AndHttp2;
            listener.UseHttps(certificates.Certificate);
        }));
        await using WebApplication worker = builder.Build();
        worker.Run(async context =>
        {
            await context.Response.WriteAsync("started");
            await context.Response.Body.FlushAsync();
            responseStarted.TrySetResult();
            await finishResponse.Task.WaitAsync(context.RequestAborted);
            await context.Response.WriteAsync("-finished");
        });
        await worker.StartAsync();
        using ServiceProvider proxyServices = new ServiceCollection()
            .AddLogging()
            .AddRpcClientServices()
            .BuildServiceProvider();
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        ChannelControl selected = new("http-always", functionGroupName: "http", isAlwaysReady: true, maxHttpConcurrency: 10)
        {
            HttpProxyService = proxyServices.GetRequiredService<Func<DelegatingHandler, IHttpProxyService>>()(
                new WorkerProxyHttpMessageHandler(RuntimeWorkerTestData.CreateCredentials("http-always"))),
            HttpUri = worker.Urls.Single(),
        };
        FunctionMetadata function = CreateHttpFunction();
        await LinkAndReadyWorkersAsync(harness, registry, function, selected);
        using MemoryStream responseBody = new();
        DefaultHttpContext httpContext = new();
        httpContext.Request.Method = "GET";
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new("runtime");
        httpContext.Items[ScriptConstants.AzureFunctionsHttpTriggerContext] = new object();
        httpContext.Response.Body = responseBody;
        Mock<IHttpResponseFeature> responseFeature = new();
        responseFeature.SetupGet(feature => feature.Headers).Returns(new HeaderDictionary());
        responseFeature.SetupProperty(feature => feature.StatusCode, StatusCodes.Status200OK);
        Func<object, Task> completeResponse = null;
        object completionState = null;
        responseFeature.Setup(feature => feature.OnCompleted(It.IsAny<Func<object, Task>>(), It.IsAny<object>()))
            .Callback((Func<object, Task> callback, object state) =>
            {
                completeResponse = callback;
                completionState = state;
            });
        httpContext.Features.Set(responseFeature.Object);
        ScriptInvocationContext invocation = CreateScaleInInvocation(function);
        invocation.Inputs = [("req", DataType.String, httpContext.Request)];
        Assert.True(selected.Channel.TryDispatchInvocation(invocation));
        Task<StreamingMessage> invocationRequest = selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        if (await Task.WhenAny(invocationRequest, invocation.ResultSource.Task) == invocation.ResultSource.Task)
        {
            await invocation.ResultSource.Task;
        }

        await invocationRequest;
        await responseStarted.Task.WaitAsync(TestTimeout);
        Task rpcResponse = selected.Channel.InvokeResponse(new()
        {
            InvocationId = invocation.ExecutionContext.InvocationId.ToString(),
            Result = new() { Status = StatusResult.Types.Status.Success },
        });
        await using CapacityAppServer appServer = new();
        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry, appServer.Configure);
        await appServer.WaitForCapacityAsync(10);
        appServer.Publish = (state, _) =>
        {
            if (state.HttpCapacity == 0)
            {
                Assert.False(selected.Channel.IsReadyForInvocations);
                Assert.False(selected.Transport.DisposeStarted.IsCompleted);
                Assert.False(invocation.ResultSource.Task.IsCompleted);
            }

            return Task.FromResult(HttpStatusCode.OK);
        };
        try
        {
            using HttpResponseMessage response = await host.PostAsync(
                """{"functionGroup":"http","isAlwaysReady":true,"desiredWorkerCount":0}""");
            Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(response));
            Assert.False(selected.Channel.IsReadyForInvocations);
            Assert.False(selected.Transport.DisposeStarted.IsCompleted);
            Assert.False(rpcResponse.IsCompleted);
            Assert.False(invocation.ResultSource.Task.IsCompleted);
        }
        finally
        {
            // Even a failed ordering assertion must release the real streaming response.
            appServer.Publish = (_, _) => Task.FromResult(HttpStatusCode.OK);
            finishResponse.TrySetResult();
        }

        await rpcResponse.WaitAsync(TestTimeout);
        Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
        Assert.Equal("started-finished", Encoding.UTF8.GetString(responseBody.ToArray()));
        Assert.False(selected.Transport.DisposeStarted.IsCompleted);
        await completeResponse(completionState);
        await selected.Transport.DisposeStarted.WaitAsync(TestTimeout);
    }

    private static ChannelControl CreateHttpWorker(string id, bool isAlwaysReady, int concurrency)
        => new(id, functionGroupName: "http", isAlwaysReady: isAlwaysReady, maxHttpConcurrency: concurrency);

    private static FunctionMetadata CreateHttpFunction()
    {
        FunctionMetadata function = new() { Name = "HttpFunction", Language = "external" };
        function.Bindings.Add(new() { Name = "req", Type = "httpTrigger", Direction = BindingDirection.In });
        return function;
    }

    private static async Task CompleteHttpWorkAsync(ChannelControl worker, ScriptInvocationContext context)
    {
        await worker.Channel.InvokeResponse(new()
        {
            InvocationId = context.ExecutionContext.InvocationId.ToString(),
            Result = new() { Status = StatusResult.Types.Status.Success },
        });
        await context.ResultSource.Task.WaitAsync(TestTimeout);
    }

    private sealed class CapacityAppServer : IAsyncDisposable
    {
        private readonly Channel<RuntimeState> _received = Channel.CreateUnbounded<RuntimeState>();
        private readonly IHost _host;
        private readonly TestServer _server;

        internal CapacityAppServer()
        {
            _host = new HostBuilder().ConfigureWebHost(builder => builder.UseTestServer().Configure(app => app.Run(async context =>
            {
                Assert.Equal("PUT", context.Request.Method);
                Assert.Equal("/admin/infra/host/state", context.Request.Path);
                Assert.Equal("test-runtime", context.Request.Headers[ScriptConstants.ContainerInstanceHeader]);
                using JsonDocument body = await JsonDocument.ParseAsync(context.Request.Body);
                RuntimeState state = new(
                    body.RootElement.GetProperty("createdTime").GetDateTimeOffset(),
                    body.RootElement.GetProperty("httpCapacity").GetInt64(),
                    body.RootElement.GetProperty("linkedWorkerCount").GetInt32());
                _received.Writer.TryWrite(state);
                context.Response.StatusCode = (int)await Publish(state, context.RequestAborted);
            }))).Start();
            _server = _host.GetTestServer();
        }

        internal Func<RuntimeState, CancellationToken, Task<HttpStatusCode>> Publish { get; set; }
            = (_, _) => Task.FromResult(HttpStatusCode.OK);

        internal IRuntimeStateManager StateManager { get; private set; }

        internal void Configure(IServiceCollection services)
        {
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CONTAINER_NAME"] = "test-runtime",
                ["MESH_INIT_URI"] = "http://appserver/",
            }).Build());
            services.AddOptions<Microsoft.Azure.WebJobs.Script.WebHost.StandbyOptions>()
                .Configure(options => options.InStandbyMode = false);
            services.AddSingleton<RuntimeStateManager>();
            services.AddSingleton<IRuntimeStateManager>(provider =>
                StateManager = provider.GetRequiredService<RuntimeStateManager>());
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<RuntimeStateManager>());
            services.AddAppServerHostStatePublisher();
            services.AddHttpClient(AppServerHostStateClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => _server.CreateHandler());
        }

        internal async Task WaitForCapacityAsync(long expected)
        {
            using CancellationTokenSource timeout = new(TestTimeout);
            while ((await _received.Reader.ReadAsync(timeout.Token)).HttpCapacity != expected)
            {
            }
        }

        public ValueTask DisposeAsync()
        {
            _host.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
