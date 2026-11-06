// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class RpcClientFunctionInvocationDispatcherTests
{
    private static void ConfigureStreamingWorker(WebApplicationBuilder builder, TestCertificateFiles certificates)
    {
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener =>
        {
            listener.Protocols = HttpProtocols.Http1AndHttp2;
            listener.UseHttps(certificates.Certificate);
        }));
        builder.Services.AddRpcClientServices();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryPostInvocation_HttpRegistrationDoesNotBlockAdmissionOrShutdown(bool shutdown)
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 2);
        FunctionMetadata function = CreateHttpFunction();
        await PrepareReadyChannelAsync(worker, function);
        using MemoryStream firstBody = new();
        using MemoryStream secondBody = new();
        ScriptInvocationContext first = CreateHttpInvocation(function, firstBody);
        ScriptInvocationContext second = CreateHttpInvocation(function, secondBody);
        TestResponseFeature response = GetResponseFeature(first);
        TaskCompletionSource registrationEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseRegistration = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IHttpResponseFeature> blockingResponse = new(MockBehavior.Strict);
        blockingResponse.Setup(feature => feature.OnCompleted(It.IsAny<Func<object, Task>>(), It.IsAny<object>()))
            .Callback<Func<object, Task>, object>((callback, state) =>
            {
                registrationEntered.TrySetResult();
                releaseRegistration.Task.GetAwaiter().GetResult();
                response.OnCompleted(callback, state);
            });
        HttpContext httpContext = ((HttpRequest)first.Inputs.Single().Val).HttpContext;
        httpContext.Features.Set(blockingResponse.Object);
        Task<bool> firstAttempt = Task.Run(() => worker.Channel.TryPostInvocation(first));
        Task<bool> secondAttempt = null;
        bool firstAccepted;
        try
        {
            await registrationEntered.Task.WaitAsync(TestTimeout);
            if (shutdown)
            {
                await Task.Run(() => worker.Channel.Shutdown(null)).WaitAsync(TestTimeout);
                Assert.True(worker.Channel.IsTerminated);
                Assert.False(worker.Channel.TryPostInvocation(second));
            }
            else
            {
                secondAttempt = Task.Run(() => worker.Channel.TryPostInvocation(second));
                Assert.True(await secondAttempt.WaitAsync(TestTimeout));
                Assert.False(worker.Channel.TryPostInvocation(CreateHttpInvocation(function, secondBody)));
            }
        }
        finally
        {
            releaseRegistration.TrySetResult();
            firstAccepted = await firstAttempt.WaitAsync(TestTimeout);
            if (secondAttempt is not null)
            {
                await secondAttempt.WaitAsync(TestTimeout);
            }
        }

        Assert.Equal(!shutdown, firstAccepted);
        httpContext.Features.Set<IHttpResponseFeature>(response);
        if (shutdown)
        {
            await response.FireCompletedAsync();
            Assert.False(worker.Channel.TryPostInvocation(CreateHttpInvocation(function, firstBody)));
        }
        else
        {
            await ReadInvocationAsync(worker);
            await ReadInvocationAsync(worker);
            await CompleteInvocationAsync(worker, first);
            await CompleteInvocationAsync(worker, second);
            await response.FireCompletedAsync();
            await GetResponseFeature(second).FireCompletedAsync();
            await worker.Channel.GetCapacityAvailableTask().WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task TryPostInvocation_HttpCompletionRegistrationFailureReturnsLease()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpFunction();
        await PrepareReadyChannelAsync(worker, function);
        using MemoryStream body = new();
        ScriptInvocationContext invocation = CreateHttpInvocation(function, body);
        HttpContext httpContext = ((HttpRequest)invocation.Inputs.Single().Val).HttpContext;
        TestResponseFeature response = GetResponseFeature(invocation);
        Mock<IHttpResponseFeature> failingResponse = new(MockBehavior.Strict);
        InvalidOperationException failure = new("Could not register HTTP completion");
        failingResponse.Setup(feature => feature.OnCompleted(It.IsAny<Func<object, Task>>(), It.IsAny<object>())).Throws(failure);
        httpContext.Features.Set(failingResponse.Object);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => worker.Channel.TryPostInvocation(invocation)));
        Assert.False(worker.Transport.Requests.TryPeek(out _));

        httpContext.Features.Set<IHttpResponseFeature>(response);
        Assert.True(worker.Channel.TryPostInvocation(invocation));
        Assert.False(worker.Channel.TryPostInvocation(CreateHttpInvocation(function, body)));
        await ReadInvocationAsync(worker);
        await CompleteInvocationAsync(worker, invocation);
        Assert.False(worker.Channel.GetCapacityAvailableTask().IsCompleted);
        await response.FireCompletedAsync();
        await worker.Channel.GetCapacityAvailableTask().WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task InvokeAsync_RealHttpStreamingHoldsSlotThroughNaturalCompletion(bool httpFinishesFirst, bool disconnect)
    {
        byte[] payload = [.. Enumerable.Range(0, 2 * 1024 * 1024).Select(i => (byte)(i % 251))];
        using TestCertificateFiles certificates = new();
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        ConfigureStreamingWorker(builder, certificates);
        await using WebApplication app = builder.Build();
        app.Run(context => context.Response.Body.WriteAsync(payload, context.RequestAborted).AsTask());
        await app.StartAsync();
        string endpoint = app.Urls.Single();
        IHttpProxyService proxy = app.Services.GetRequiredService<Func<DelegatingHandler, IHttpProxyService>>()(
            new WorkerProxyHttpMessageHandler(RuntimeWorkerTestData.CreateCredentials()));
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", advertisedHttpUri: endpoint, httpProxyService: proxy, functionGroupName: "http", maxHttpConcurrency: "1");
        FunctionMetadata function = CreateHttpFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        using CancellationTokenSource requestAborted = new();
        using GatedResponseStream stream = new();
        GatedResponseBodyFeature responseBody = new(stream);
        ScriptInvocationContext first = CreateHttpInvocation(function, stream, requestAborted.Token);
        HttpRequest request = (HttpRequest)first.Inputs.Single().Val;
        request.HttpContext.Features.Set<IHttpResponseBodyFeature>(responseBody);
        using MemoryStream secondBody = new();
        ScriptInvocationContext second = CreateHttpInvocation(function, secondBody);
        Task responseHandling = Task.CompletedTask;

        try
        {
            await dispatcher.InvokeAsync(first);
            await ReadInvocationAsync(worker);
            await stream.WriteEntered.Task.WaitAsync(TestTimeout);
            Task waiting = dispatcher.InvokeAsync(second);
            Assert.False(waiting.IsCompleted);
            Assert.Equal(1, GetResponseFeature(first).CompletionCallbackCount);
            Assert.Equal(0, GetResponseFeature(second).CompletionCallbackCount);

            if (disconnect)
            {
                requestAborted.Cancel();
                await ((Task)first.Properties[ScriptConstants.HttpProxyTask]).WaitAsync(TestTimeout);
                Assert.False(waiting.IsCompleted);
                await GetResponseFeature(first).FireCompletedAsync();
                Assert.False(waiting.IsCompleted);
            }
            else if (httpFinishesFirst)
            {
                stream.ReleaseWrites.TrySetResult();
                await ((Task)first.Properties[ScriptConstants.HttpProxyTask]).WaitAsync(TestTimeout);
                await GetResponseFeature(first).FireCompletedAsync();
                Assert.False(waiting.IsCompleted);
            }

            responseHandling = CompleteInvocationAsync(worker, first);
            if (!httpFinishesFirst && !disconnect)
            {
                Assert.False(responseHandling.IsCompleted);
                Assert.False(waiting.IsCompleted);
            }

            stream.ReleaseWrites.TrySetResult();
            await responseHandling.WaitAsync(TestTimeout);
            Assert.NotNull(await first.ResultSource.Task.WaitAsync(TestTimeout));
            if (!disconnect)
            {
                Assert.Equal(payload, stream.ToArray());
            }

            if (!httpFinishesFirst && !disconnect)
            {
                // Invocation completion must unblock the middleware while the slot remains held.
                Assert.False(waiting.IsCompleted);
                Assert.False(worker.Transport.Requests.TryPeek(out _));
                await GetResponseFeature(first).FireCompletedAsync();
            }

            await waiting.WaitAsync(TestTimeout);
            Assert.Equal(1, GetResponseFeature(second).CompletionCallbackCount);
            await ReadInvocationAsync(worker);
            await GetResponseFeature(first).FireCompletedAsync();
            using CancellationTokenSource canceledWait = new();
            using MemoryStream thirdBody = new();
            ScriptInvocationContext third = CreateHttpInvocation(function, thirdBody);
            third.CancellationToken = canceledWait.Token;
            Task thirdWaiting = dispatcher.InvokeAsync(third);
            Assert.False(thirdWaiting.IsCompleted);
            canceledWait.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => thirdWaiting.WaitAsync(TestTimeout));

            await CompleteInvocationAsync(worker, second).WaitAsync(TestTimeout);
            Assert.NotNull(await second.ResultSource.Task.WaitAsync(TestTimeout));
            Assert.Equal(payload.Length, secondBody.Length);
            Task released = worker.Channel.GetCapacityAvailableTask();
            await GetResponseFeature(second).FireCompletedAsync();
            await released.WaitAsync(TestTimeout);
            Assert.False(worker.Transport.Requests.TryPeek(out _));
        }
        finally
        {
            stream.ReleaseWrites.TrySetResult();
            requestAborted.Cancel();
            await responseHandling.WaitAsync(TestTimeout);
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_NaturalCompletionPreservesPostInvocationMiddleware(bool disconnect)
    {
        byte[] payload = new byte[2 * 1024 * 1024];
        const string tail = "written by middleware after invocation completion";
        using TestCertificateFiles certificates = new();
        WebApplicationBuilder workerBuilder = WebApplication.CreateBuilder();
        workerBuilder.Logging.ClearProviders();
        ConfigureStreamingWorker(workerBuilder, certificates);
        await using WebApplication workerApp = workerBuilder.Build();
        workerApp.Run(context => context.Response.Body.WriteAsync(payload, context.RequestAborted).AsTask());
        await workerApp.StartAsync();
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", advertisedHttpUri: workerApp.Urls.Single(),
            httpProxyService: workerApp.Services.GetRequiredService<Func<DelegatingHandler, IHttpProxyService>>()(
                new WorkerProxyHttpMessageHandler(RuntimeWorkerTestData.CreateCredentials())),
            functionGroupName: "http", maxHttpConcurrency: "1");
        FunctionMetadata function = CreateHttpFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        TaskCompletionSource<ScriptInvocationContext> firstInvocation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<ScriptInvocationContext> secondInvocation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Task> secondDispatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource postInvocationWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releasePipeline = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WebApplicationBuilder hostBuilder = WebApplication.CreateBuilder();
        hostBuilder.Logging.ClearProviders();
        hostBuilder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using WebApplication hostApp = hostBuilder.Build();
        hostApp.Use(async (HttpContext context, RequestDelegate next) =>
        {
            await next(context);
            if (string.Equals(context.Request.Path.Value, "/first", StringComparison.Ordinal))
            {
                await context.Response.WriteAsync(tail, context.RequestAborted);
                postInvocationWrite.TrySetResult();
                await releasePipeline.Task.WaitAsync(context.RequestAborted);
            }
        });
        hostApp.Run(async context =>
        {
            context.Items[ScriptConstants.AzureFunctionsHttpTriggerContext] = new();
            ScriptInvocationContext invocation = CreateInvocation(function, context.RequestAborted);
            invocation.Inputs = [("req", DataType.String, context.Request)];
            bool first = string.Equals(context.Request.Path.Value, "/first", StringComparison.Ordinal);
            Task dispatch = dispatcher.InvokeAsync(invocation);
            if (first)
            {
                firstInvocation.TrySetResult(invocation);
            }
            else
            {
                secondInvocation.TrySetResult(invocation);
                secondDispatch.TrySetResult(dispatch);
            }

            await dispatch;
            await invocation.ResultSource.Task;
        });
        await hostApp.StartAsync();
        using HttpClient client = new() { Timeout = TestTimeout };
        using CancellationTokenSource firstRequestCancellation = new();

        try
        {
            Task<HttpResponseMessage> firstResponse = client.GetAsync(hostApp.Urls.Single() + "/first", firstRequestCancellation.Token);
            ScriptInvocationContext first = await firstInvocation.Task.WaitAsync(TestTimeout);
            await ReadInvocationAsync(worker);
            Task<HttpResponseMessage> secondResponse = client.GetAsync(hostApp.Urls.Single() + "/second");
            Task waiting = await secondDispatch.Task.WaitAsync(TestTimeout);
            Assert.False(waiting.IsCompleted);

            await CompleteInvocationAsync(worker, first).WaitAsync(TestTimeout);
            await postInvocationWrite.Task.WaitAsync(TestTimeout);
            Assert.True(first.ResultSource.Task.IsCompletedSuccessfully);
            Assert.False(waiting.IsCompleted);
            Assert.False(worker.Transport.Requests.TryPeek(out _));

            if (disconnect)
            {
                firstRequestCancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstResponse.WaitAsync(TestTimeout));
            }
            else
            {
                releasePipeline.TrySetResult();
                using HttpResponseMessage response = await firstResponse.WaitAsync(TestTimeout);
                response.EnsureSuccessStatusCode();
                byte[] content = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal(payload.Length + Encoding.UTF8.GetByteCount(tail), content.Length);
                Assert.Equal(tail, Encoding.UTF8.GetString(content.AsSpan(payload.Length)));
            }

            await waiting.WaitAsync(TestTimeout);
            await ReadInvocationAsync(worker);
            await CompleteInvocationAsync(worker, await secondInvocation.Task.WaitAsync(TestTimeout)).WaitAsync(TestTimeout);
            using HttpResponseMessage secondResult = await secondResponse.WaitAsync(TestTimeout);
            secondResult.EnsureSuccessStatusCode();
            Assert.Equal(payload, await secondResult.Content.ReadAsByteArrayAsync());
            Assert.False(worker.Transport.Requests.TryPeek(out _));
        }
        finally
        {
            releasePipeline.TrySetResult();
            firstRequestCancellation.Cancel();
            client.CancelPendingRequests();
            worker.Channel.Shutdown(new InvalidOperationException("Test cleanup"));
            await hostApp.StopAsync();
            await workerApp.StopAsync();
        }
    }

    [Fact]
    public async Task InvokeAsync_ConcurrentHttpAndWorkerCompletionReleaseExactlyOnce()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpFunction();
        await PrepareReadyChannelAsync(worker, function);

        for (int i = 0; i < 20; i++)
        {
            using MemoryStream body = new();
            ScriptInvocationContext invocation = CreateHttpInvocation(function, body);
            Assert.True(worker.Channel.TryPostInvocation(invocation));
            await ReadInvocationAsync(worker);
            Task released = worker.Channel.GetCapacityAvailableTask();

            await Task.WhenAll(
                Task.Run(() => CompleteInvocationAsync(worker, invocation)),
                Task.Run(() => GetResponseFeature(invocation).FireCompletedAsync())).WaitAsync(TestTimeout);

            Assert.True(invocation.ResultSource.Task.IsCompletedSuccessfully);
            await released.WaitAsync(TestTimeout);
            await GetResponseFeature(invocation).FireCompletedAsync();
            await worker.Channel.GetCapacityAvailableTask().WaitAsync(TestTimeout);

            ScriptInvocationContext probe = CreateHttpInvocation(function, body);
            Assert.True(worker.Channel.TryPostInvocation(probe));
            Task nextAvailable = worker.Channel.GetCapacityAvailableTask();
            Assert.False(nextAvailable.IsCompleted);
            Assert.False(worker.Channel.TryPostInvocation(CreateHttpInvocation(function, body)));
            await GetResponseFeature(invocation).FireCompletedAsync();
            Assert.False(nextAvailable.IsCompleted);
            Assert.False(worker.Channel.TryPostInvocation(CreateHttpInvocation(function, body)));
            await ReadInvocationAsync(worker);
            await CompleteInvocationAsync(worker, probe);
            await GetResponseFeature(probe).FireCompletedAsync();
        }
    }

    [Fact]
    public async Task InvokeAsync_LateHttpCompletionAfterShutdownDoesNotReleaseAgain()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpFunction();
        await PrepareReadyChannelAsync(worker, function);
        using MemoryStream body = new();
        ScriptInvocationContext invocation = CreateHttpInvocation(function, body);
        Assert.True(worker.Channel.TryPostInvocation(invocation));
        await ReadInvocationAsync(worker);
        await CompleteInvocationAsync(worker, invocation);
        Assert.True(invocation.ResultSource.Task.IsCompletedSuccessfully);

        worker.Channel.Shutdown(new InvalidOperationException("Worker terminated"));
        Task available = worker.Channel.GetCapacityAvailableTask();
        await GetResponseFeature(invocation).FireCompletedAsync();
        await GetResponseFeature(invocation).FireCompletedAsync();

        await available.WaitAsync(TestTimeout);
        Assert.False(worker.Channel.IsReadyForInvocations);
        Assert.False(worker.Channel.TryPostInvocation(CreateHttpInvocation(function, body)));
        await worker.Channel.DrainInvocationsAsync().WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task InvokeAsync_ManualHttpFunctionDoesNotWaitForAnUnownedResponse()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        DefaultHttpContext manual = new();
        TestResponseFeature response = new();
        manual.Features.Set<IHttpResponseFeature>(response);
        manual.Request.Method = "GET";
        manual.Request.Host = new("manual.example");
        ScriptInvocationContext first = CreateInvocation(function);
        first.Inputs = [("req", DataType.String, manual.Request)];
        await dispatcher.InvokeAsync(first);
        await ReadInvocationAsync(worker);
        await CompleteInvocationAsync(worker, first);
        Assert.True(first.ResultSource.Task.IsCompletedSuccessfully);
        Assert.Equal(0, response.CompletionCallbackCount);

        ScriptInvocationContext second = CreateInvocation(function);
        second.Inputs = [("req", DataType.String, manual.Request)];
        await dispatcher.InvokeAsync(second).WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
        await CompleteInvocationAsync(worker, second);
        await worker.Channel.DrainInvocationsAsync().WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task InvokeAsync_HttpOverGrpcHoldsSlotUntilResponseCompletion()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        using MemoryStream firstBody = new();
        using MemoryStream secondBody = new();
        ScriptInvocationContext first = CreateHttpInvocation(function, firstBody);
        ScriptInvocationContext second = CreateHttpInvocation(function, secondBody);
        await dispatcher.InvokeAsync(first);
        await ReadInvocationAsync(worker);
        Task waiting = dispatcher.InvokeAsync(second);
        await CompleteInvocationAsync(worker, first);
        Assert.True(first.ResultSource.Task.IsCompletedSuccessfully);
        Assert.False(waiting.IsCompleted);

        await GetResponseFeature(first).FireCompletedAsync();
        await waiting.WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
        await CompleteInvocationAsync(worker, second);
        Assert.True(second.ResultSource.Task.IsCompletedSuccessfully);
        await GetResponseFeature(second).FireCompletedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_HttpPreSendFailureHoldsSlotUntilErrorResponseCompletes(bool cancelBeforeSend)
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpFunction();
        await PrepareReadyChannelAsync(worker, function, succeeded: cancelBeforeSend);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        using MemoryStream firstBody = new();
        using MemoryStream secondBody = new();
        ScriptInvocationContext first = CreateHttpInvocation(function, firstBody);
        ScriptInvocationContext second = CreateHttpInvocation(function, secondBody);
        if (cancelBeforeSend)
        {
            first.CancellationToken = new(canceled: true);
        }

        Assert.True(worker.Channel.TryPostInvocation(first));
        Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => first.ResultSource.Task.WaitAsync(TestTimeout));
        Assert.IsNotType<TimeoutException>(exception);
        Assert.False(worker.Transport.Requests.TryPeek(out _));
        Task waiting = dispatcher.InvokeAsync(second);
        Assert.False(waiting.IsCompleted);

        await GetResponseFeature(first).FireCompletedAsync();
        await waiting.WaitAsync(TestTimeout);
        if (cancelBeforeSend)
        {
            await ReadInvocationAsync(worker);
            await CompleteInvocationAsync(worker, second);
            Assert.True(second.ResultSource.Task.IsCompletedSuccessfully);
        }
        else
        {
            Exception secondException = await Assert.ThrowsAnyAsync<Exception>(() => second.ResultSource.Task.WaitAsync(TestTimeout));
            Assert.IsNotType<TimeoutException>(secondException);
        }

        await GetResponseFeature(second).FireCompletedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_OutboundFailureAfterForwardingStartsRetainsSlotUntilHttpTerminates(bool failForwarding)
    {
        TaskCompletionSource forwarding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource forwardingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IHttpProxyService> proxy = new(MockBehavior.Strict);
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", advertisedHttpUri: "https://worker:8080", httpProxyService: proxy.Object,
            functionGroupName: "http", maxHttpConcurrency: "1");
        proxy.Setup(service => service.StartForwarding(It.IsAny<ScriptInvocationContext>(), It.IsAny<Uri>()))
            .Callback<ScriptInvocationContext, Uri>((context, _) =>
            {
                context.Properties[ScriptConstants.HttpProxyTask] = forwarding.Task;
                worker.Transport.Writer.TryComplete();
                forwardingStarted.TrySetResult();
            });
        FunctionMetadata function = CreateHttpFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        using MemoryStream firstBody = new();
        ScriptInvocationContext first = CreateHttpInvocation(function, firstBody);
        await dispatcher.InvokeAsync(first);
        await forwardingStarted.Task.WaitAsync(TestTimeout);
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.ResultSource.Task.WaitAsync(TestTimeout));
        Assert.False(worker.Channel.IsTerminated);
        using MemoryStream secondBody = new();
        ScriptInvocationContext second = CreateHttpInvocation(function, secondBody);
        Task waiting = dispatcher.InvokeAsync(second);
        Assert.False(waiting.IsCompleted);
        await GetResponseFeature(first).FireCompletedAsync();
        Assert.False(waiting.IsCompleted);

        if (failForwarding)
        {
            forwarding.TrySetException(new IOException("Forwarding failed after outbound write failure"));
        }
        else
        {
            forwarding.TrySetResult();
        }

        await waiting.WaitAsync(TestTimeout);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.ResultSource.Task.WaitAsync(TestTimeout));
        Task released = worker.Channel.GetCapacityAvailableTask();
        await GetResponseFeature(second).FireCompletedAsync();
        await released.WaitAsync(TestTimeout);
        await worker.Channel.DrainInvocationsAsync().WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_HttpFailureOrDisconnectBeforeGrpcDoesNotReleaseSlot(bool disconnect)
    {
        TaskCompletionSource forwarding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IHttpProxyService> proxy = new(MockBehavior.Strict);
        proxy.Setup(service => service.StartForwarding(It.IsAny<ScriptInvocationContext>(), It.IsAny<Uri>()))
            .Callback<ScriptInvocationContext, Uri>((context, _) => context.Properties[ScriptConstants.HttpProxyTask] = forwarding.Task);
        proxy.Setup(service => service.EnsureSuccessfulForwardingAsync(It.IsAny<ScriptInvocationContext>()))
            .Returns(forwarding.Task);
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", advertisedHttpUri: "https://worker:8080", httpProxyService: proxy.Object,
            functionGroupName: "http", maxHttpConcurrency: "1");
        FunctionMetadata function = CreateHttpFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        using MemoryStream body = new();
        using CancellationTokenSource aborted = new();
        ScriptInvocationContext first = CreateHttpInvocation(function, body, aborted.Token);
        await dispatcher.InvokeAsync(first);
        await ReadInvocationAsync(worker);
        if (disconnect)
        {
            aborted.Cancel();
            forwarding.TrySetCanceled(aborted.Token);
        }
        else
        {
            forwarding.TrySetException(new IOException("HTTP forwarding failed"));
        }

        using MemoryStream secondBody = new();
        Task waiting = dispatcher.InvokeAsync(CreateHttpInvocation(function, secondBody));
        Assert.False(waiting.IsCompleted);
        Assert.False(worker.Transport.Requests.TryPeek(out _));
        await GetResponseFeature(first).FireCompletedAsync();
        Assert.False(waiting.IsCompleted);
        await CompleteInvocationAsync(worker, first).WaitAsync(TestTimeout);
        await Assert.ThrowsAnyAsync<Exception>(() => first.ResultSource.Task.WaitAsync(TestTimeout));
        await waiting.WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
    }

    private static FunctionMetadata CreateHttpFunction()
    {
        FunctionMetadata function = CreateFunction();
        function.Bindings.Add(new() { Name = "req", Type = "httpTrigger", Direction = BindingDirection.In });
        return function;
    }

    private static ScriptInvocationContext CreateHttpInvocation(FunctionMetadata function, Stream responseBody, CancellationToken aborted = default)
    {
        ScriptInvocationContext invocation = CreateInvocation(function);
        DefaultHttpContext context = new();
        context.Features.Set<IHttpResponseFeature>(new TestResponseFeature());
        context.Items[ScriptConstants.AzureFunctionsHttpTriggerContext] = new();
        context.Request.Method = "GET";
        context.Request.Host = new("customer.example");
        context.Request.Path = "/api/stream";
        context.RequestAborted = aborted;
        context.Response.Body = responseBody;
        invocation.Inputs = [("req", DataType.String, context.Request)];
        return invocation;
    }

    private static TestResponseFeature GetResponseFeature(ScriptInvocationContext invocation)
    {
        HttpRequest request = Assert.IsAssignableFrom<HttpRequest>(Assert.Single(invocation.Inputs).Val);
        return Assert.IsType<TestResponseFeature>(request.HttpContext.Features.Get<IHttpResponseFeature>());
    }

    private sealed class TestResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];

        internal int CompletionCallbackCount => _callbacks.Count;

        public override void OnCompleted(Func<object, Task> callback, object state) => _callbacks.Add((callback, state));

        internal async Task FireCompletedAsync()
        {
            // Retain callbacks so tests can also exercise duplicate and late completion notifications.
            foreach ((Func<object, Task> callback, object state) in _callbacks)
            {
                await callback(state);
            }
        }
    }

    private sealed class GatedResponseStream : MemoryStream
    {
        internal TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ReleaseWrites { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteEntered.TrySetResult();
            await ReleaseWrites.Task.WaitAsync(cancellationToken);
            await base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class GatedResponseBodyFeature(Stream stream) : IHttpResponseBodyFeature
    {
        public Stream Stream { get; } = stream;

        public PipeWriter Writer { get; } = PipeWriter.Create(stream, new(leaveOpen: true));

        public void DisableBuffering() { }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CompleteAsync() => throw new InvalidOperationException("The worker channel must not finalize the HTTP response.");
    }
}
