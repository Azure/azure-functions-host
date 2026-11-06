// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using CapabilitiesUpdateStrategy = Microsoft.Azure.WebJobs.Script.Grpc.Messages.FunctionEnvironmentReloadResponse.Types.CapabilitiesUpdateStrategy;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class RpcClientHttpInvocationTests
{
    private static readonly Uri HttpEndpoint = new("https://platform-proxy:28080");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("https://platform-proxy:28080")]
    [InlineData("https://worker-proxy:48801")]
    public async Task InvokeAsync_UsesAuthenticatedNegotiatedHttpsOrigin(string advertisedHttpUri)
    {
        Uri endpoint = new(advertisedHttpUri);
        Mock<IHttpProxyService> proxy = CreateProxy(endpoint);
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", advertisedHttpUri: advertisedHttpUri, httpProxyService: proxy.Object);
        FunctionMetadata function = await LoadFunctionAsync(worker);

        await InvokeHttpAsync(worker, function, proxy, endpoint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task InvokeAsync_WithoutHttpCapabilitySendsHttpPayloadOverGrpc(string advertisedHttpUri)
    {
        Mock<IHttpProxyService> proxy = new(MockBehavior.Strict);
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", advertisedHttpUri: advertisedHttpUri, httpProxyService: proxy.Object);
        FunctionMetadata function = await LoadFunctionAsync(worker);

        await InvokeHttpOverGrpcAsync(worker, function, proxy);
    }

    [Theory]
    [InlineData(CapabilitiesUpdateStrategy.Merge, "https://platform-proxy:28080")]
    [InlineData(CapabilitiesUpdateStrategy.Replace, "https://platform-proxy:28080")]
    public async Task InvokeAsync_CapabilityReloadPreservesNegotiatedProxyOrigin(CapabilitiesUpdateStrategy strategy, string advertisedHttpUri)
    {
        Uri endpoint = HttpEndpoint;
        Mock<IHttpProxyService> proxy = CreateProxy(endpoint);
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", advertisedHttpUri: HttpEndpoint.AbsoluteUri, httpProxyService: proxy.Object);
        await ReloadCapabilitiesAsync(worker, strategy, advertisedHttpUri);
        FunctionMetadata function = await LoadFunctionAsync(worker);

        await InvokeHttpAsync(worker, function, proxy, endpoint);
    }

    [Theory]
    [InlineData(CapabilitiesUpdateStrategy.Merge)]
    [InlineData(CapabilitiesUpdateStrategy.Replace)]
    public async Task InvokeAsync_CapabilityReloadCanEnableHttpProxying(CapabilitiesUpdateStrategy strategy)
    {
        Mock<IHttpProxyService> proxy = CreateProxy(HttpEndpoint);
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", httpProxyService: proxy.Object);
        await ReloadCapabilitiesAsync(worker, strategy, HttpEndpoint.AbsoluteUri);
        FunctionMetadata function = await LoadFunctionAsync(worker);

        await InvokeHttpAsync(worker, function, proxy, HttpEndpoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_NonHttpInvocationDoesNotUseHttpProxy(bool hasHttpCapability)
    {
        Mock<IHttpProxyService> proxy = new(MockBehavior.Strict);
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", advertisedHttpUri: hasHttpCapability ? HttpEndpoint.AbsoluteUri : null, httpProxyService: proxy.Object);
        FunctionMetadata function = await LoadFunctionAsync(worker, "timerTrigger");
        ScriptInvocationContext invocation = CreateInvocation(function);

        Assert.True(worker.Channel.TryPostInvocation(invocation));
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);

        Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
        proxy.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admission_HoldsCapacityUntilProxyAndResponseBodyComplete(bool alwaysReady)
    {
        Mock<IHttpProxyService> proxy = CreateProxy(HttpEndpoint);
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", advertisedHttpUri: HttpEndpoint.AbsoluteUri, httpProxyService: proxy.Object,
            functionGroupName: FunctionGroups.Http, maxHttpConcurrency: "1", isAlwaysReady: alwaysReady);
        FunctionMetadata function = await LoadFunctionAsync(worker);
        var (invocation, _, completeResponse) = CreateHttpInvocation(function);
        TaskCompletionSource proxyCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        invocation.Properties[ScriptConstants.HttpProxyTask] = proxyCompleted.Task;
        Assert.True(TryDispatch(worker, invocation, out _));
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        ScriptInvocationContext next = CreateInvocation(function);
        Assert.False(TryDispatch(worker, next, out Task capacityChanged));

        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);
        await invocation.ResultSource.Task.WaitAsync(TestTimeout);
        Assert.False(TryDispatch(worker, next, out _));

        proxyCompleted.SetResult();
        Assert.False(TryDispatch(worker, next, out _));
        await completeResponse();
        await capacityChanged.WaitAsync(TestTimeout);

        Assert.True(TryDispatch(worker, next, out _));
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admission_DisconnectDoesNotReleaseExecutingInvocation(bool succeeded)
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", functionGroupName: FunctionGroups.Http, maxHttpConcurrency: "1");
        FunctionMetadata function = await LoadFunctionAsync(worker);
        var (invocation, httpContext, _) = CreateHttpInvocation(function);
        using CancellationTokenSource aborted = new();
        httpContext.RequestAborted = aborted.Token;
        Assert.True(TryDispatch(worker, invocation, out _));
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);

        aborted.Cancel();
        Assert.False(invocation.ResultSource.Task.IsCompleted);
        ScriptInvocationContext next = CreateInvocation(function);
        Assert.False(TryDispatch(worker, next, out Task capacityChanged));
        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId, succeeded);
        if (succeeded)
        {
            await invocation.ResultSource.Task.WaitAsync(TestTimeout);
        }
        else
        {
            await Assert.ThrowsAnyAsync<Exception>(() => invocation.ResultSource.Task.WaitAsync(TestTimeout));
        }

        await capacityChanged.WaitAsync(TestTimeout);
        Assert.True(TryDispatch(worker, next, out _));
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admission_DeclinedAttributionOrPostDoesNotLeakCapacity(bool declineBuffer)
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", functionGroupName: FunctionGroups.Http, maxHttpConcurrency: "1");
        FunctionMetadata function = await LoadFunctionAsync(worker);
        var (invocation, _, completeResponse) = CreateHttpInvocation(function);
        if (declineBuffer)
        {
            worker.Channel.FunctionInputBuffers[function.GetFunctionId()].Complete();
        }

        Assert.True(worker.Channel.TryDispatchInvocation(invocation, post => declineBuffer && post(), out _));
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.ResultSource.Task.WaitAsync(TestTimeout));
        ScriptInvocationContext next = CreateInvocation(function);
        bool callbackRan = false;
        Assert.True(worker.Channel.TryDispatchInvocation(next, _ =>
        {
            callbackRan = true;
            return false;
        }, out _));

        Assert.True(callbackRan);
        await Assert.ThrowsAsync<InvalidOperationException>(() => next.ResultSource.Task);
        if (declineBuffer)
        {
            await completeResponse();
        }

        await worker.Channel.DrainInvocationsAsync().WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admission_CancellationRetainsCapacityUntilInvocationAndResponseComplete(bool beforeSend)
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", functionGroupName: FunctionGroups.Http, maxHttpConcurrency: "1",
            handlesInvocationCancel: !beforeSend);
        FunctionMetadata function = await LoadFunctionAsync(worker);
        var (invocation, _, completeResponse) = CreateHttpInvocation(function);
        using CancellationTokenSource cancellation = new();
        invocation.CancellationToken = cancellation.Token;
        if (beforeSend)
        {
            cancellation.Cancel();
        }

        Assert.True(TryDispatch(worker, invocation, out _));
        if (beforeSend)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation.ResultSource.Task.WaitAsync(TestTimeout));
        }
        else
        {
            StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
            cancellation.Cancel();
            await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationCancel);
            Assert.False(invocation.ResultSource.Task.IsCompleted);
            Assert.False(TryDispatch(worker, CreateInvocation(function), out _));
            await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);
            await invocation.ResultSource.Task.WaitAsync(TestTimeout);
        }

        ScriptInvocationContext next = CreateInvocation(function);
        Assert.False(TryDispatch(worker, next, out Task capacityChanged));
        await completeResponse();
        await capacityChanged.WaitAsync(TestTimeout);
        Assert.True(TryDispatch(worker, next, out _));
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Fact]
    public async Task Admission_DuplicateInvocationIdDoesNotReleaseAnotherInvocationsCapacity()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", functionGroupName: FunctionGroups.Http, maxHttpConcurrency: "2");
        FunctionMetadata function = await LoadFunctionAsync(worker);
        ScriptInvocationContext first = CreateInvocation(function);
        Assert.True(TryDispatch(worker, first, out _));
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        ScriptInvocationContext duplicate = CreateInvocation(function);
        duplicate.ExecutionContext.InvocationId = first.ExecutionContext.InvocationId;

        Assert.True(TryDispatch(worker, duplicate, out _));
        await Assert.ThrowsAsync<InvalidOperationException>(() => duplicate.ResultSource.Task);
        Assert.True(TryDispatch(worker, CreateInvocation(function), out _));
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        ScriptInvocationContext next = CreateInvocation(function);
        Assert.False(TryDispatch(worker, next, out Task capacityChanged));

        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);
        await capacityChanged.WaitAsync(TestTimeout);
        Assert.True(TryDispatch(worker, next, out _));
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    private static bool TryDispatch(ClientWorkerChannelTestHarness worker, ScriptInvocationContext invocation, out Task capacityChanged)
        => worker.Channel.TryDispatchInvocation(invocation, post => post(), out capacityChanged);

    private static (ScriptInvocationContext Invocation, HttpContext HttpContext, Func<Task> CompleteResponse) CreateHttpInvocation(
        FunctionMetadata function)
    {
        ScriptInvocationContext invocation = CreateInvocation(function);
        DefaultHttpContext httpContext = new();
        httpContext.Request.Method = "GET";
        httpContext.Request.Host = new("incoming.example");
        httpContext.Items.Add(ScriptConstants.AzureFunctionsHttpTriggerContext, null);
        Func<Task> completeResponse = null;
        Mock<IHttpResponseFeature> response = new();
        response.Setup(feature => feature.OnCompleted(It.IsAny<Func<object, Task>>(), It.IsAny<object>()))
            .Callback((Func<object, Task> callback, object state) => completeResponse = () => callback(state));
        httpContext.Features.Set(response.Object);
        invocation.Inputs = [("req", DataType.String, httpContext.Request)];

        return (invocation, httpContext, () => completeResponse());
    }

    private static Mock<IHttpProxyService> CreateProxy(Uri endpoint)
    {
        Mock<IHttpProxyService> proxy = new(MockBehavior.Strict);
        proxy.Setup(service => service.StartForwarding(It.IsAny<ScriptInvocationContext>(), endpoint));
        proxy.Setup(service => service.EnsureSuccessfulForwardingAsync(It.IsAny<ScriptInvocationContext>()))
            .Returns(Task.CompletedTask);

        return proxy;
    }

    [Theory]
    [InlineData("http://localhost:5678/customer", typeof(WorkerLinkException))]
    [InlineData("https://different-customer:48801", typeof(WorkerLinkException))]
    public async Task CapabilityReload_RejectsUnrewrittenCustomerOriginBeforeAnyCredentialForwarding(string destination, Type exceptionType)
    {
        Mock<IHttpProxyService> proxy = CreateProxy(HttpEndpoint);
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", advertisedHttpUri: HttpEndpoint.AbsoluteUri, httpProxyService: proxy.Object);
        Task<bool> reload = worker.Channel.SendFunctionEnvironmentReloadRequest();
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionEnvironmentReloadRequest);
        await worker.Transport.SendResponseAsync(new()
        {
            FunctionEnvironmentReloadResponse = new()
            {
                Result = new() { Status = StatusResult.Types.Status.Success },
                Capabilities = { ["HttpUri"] = destination }
            }
        });
        await Assert.ThrowsAsync(exceptionType, () => reload.WaitAsync(TestTimeout));
        proxy.VerifyNoOtherCalls();
        FunctionMetadata function = await LoadFunctionAsync(worker);
        await InvokeHttpAsync(worker, function, proxy, HttpEndpoint);
    }

    private static async Task InvokeHttpAsync(
        ClientWorkerChannelTestHarness worker, FunctionMetadata function, Mock<IHttpProxyService> proxy, Uri endpoint)
    {
        ScriptInvocationContext invocation = CreateInvocation(function);
        using MemoryStream body = new(Encoding.UTF8.GetBytes("HTTP body must not be serialized over gRPC"));
        HttpRequest httpRequest = new DefaultHttpContext().Request;
        httpRequest.HttpContext.Items[ScriptConstants.AzureFunctionsHttpTriggerContext] = new object();
        httpRequest.Method = "POST";
        httpRequest.Host = new("incoming.example");
        httpRequest.Body = body;
        httpRequest.ContentType = "text/plain";
        invocation.Inputs = [("req", DataType.String, httpRequest)];

        Assert.True(worker.Channel.TryPostInvocation(invocation));
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);

        Assert.Equal(new RpcHttp(), Assert.Single(request.InvocationRequest.InputData).Data.Http);
        proxy.Verify(service => service.StartForwarding(invocation, endpoint), Times.Once);
        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);
        Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
        proxy.Verify(service => service.EnsureSuccessfulForwardingAsync(invocation), Times.Once);
        proxy.VerifyNoOtherCalls();
    }

    private static async Task InvokeHttpOverGrpcAsync(
        ClientWorkerChannelTestHarness worker, FunctionMetadata function, Mock<IHttpProxyService> proxy)
    {
        ScriptInvocationContext invocation = CreateInvocation(function);
        const string content = "HTTP body serialized over gRPC";
        using MemoryStream body = new(Encoding.UTF8.GetBytes(content));
        HttpRequest httpRequest = new DefaultHttpContext().Request;
        httpRequest.Method = "POST";
        httpRequest.Host = new("incoming.example");
        httpRequest.Path = "/api/test";
        httpRequest.Body = body;
        httpRequest.ContentType = "text/plain";
        httpRequest.ContentLength = body.Length;
        invocation.Inputs = [("req", DataType.String, httpRequest)];

        Assert.True(worker.Channel.TryPostInvocation(invocation));
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        RpcHttp http = Assert.Single(request.InvocationRequest.InputData).Data.Http;

        Assert.Equal("POST", http.Method);
        Assert.Equal("http://incoming.example/api/test", http.Url);
        Assert.Equal(content, http.Body.String);
        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);
        Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
        proxy.VerifyNoOtherCalls();
    }

    private static async Task ReloadCapabilitiesAsync(
        ClientWorkerChannelTestHarness worker, CapabilitiesUpdateStrategy strategy, string advertisedHttpUri)
    {
        Task<bool> reload = worker.Channel.SendFunctionEnvironmentReloadRequest();
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionEnvironmentReloadRequest);
        FunctionEnvironmentReloadResponse response = new()
        {
            Result = new() { Status = StatusResult.Types.Status.Success },
            CapabilitiesUpdateStrategy = strategy,
        };
        if (advertisedHttpUri is not null)
        {
            response.Capabilities.Add(RpcWorkerConstants.HttpUri, advertisedHttpUri);
        }

        await worker.Transport.SendResponseAsync(new() { FunctionEnvironmentReloadResponse = response });

        Assert.True(await reload.WaitAsync(TestTimeout));
    }

    private static async Task<FunctionMetadata> LoadFunctionAsync(ClientWorkerChannelTestHarness worker, string trigger = "httpTrigger")
    {
        FunctionMetadata function = new() { Name = "TestFunction", Language = "external" };
        function.Bindings.Add(new() { Name = "req", Type = trigger, Direction = BindingDirection.In });
        worker.Channel.SetupFunctionInvocationBuffers([function]);
        worker.Channel.SendFunctionLoadRequests(new ManagedDependencyOptions(), null);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await worker.SendFunctionLoadResponseAsync(function.GetFunctionId());

        return function;
    }

    private static ScriptInvocationContext CreateInvocation(FunctionMetadata function)
        => new()
        {
            FunctionMetadata = function,
            ExecutionContext = new() { FunctionName = function.Name, InvocationId = Guid.NewGuid() },
            BindingData = [],
            Inputs = [],
            ResultSource = new(TaskCreationOptions.RunContinuationsAsynchronously),
            AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),
            Logger = NullLogger.Instance,
        };
}
