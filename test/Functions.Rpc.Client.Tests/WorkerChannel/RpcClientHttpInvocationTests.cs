// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.AspNetCore.Http;
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
    private static readonly Uri HttpEndpoint = new("http://platform-proxy:28080");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://worker-advertised:8080")]
    [InlineData("not-an-absolute-uri")]
    public async Task InvokeAsync_UsesLinkHttpEndpointInsteadOfWorkerCapability(string advertisedHttpUri)
    {
        Mock<IHttpProxyService> proxy = CreateProxy();
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", httpEndpoint: HttpEndpoint, advertisedHttpUri: advertisedHttpUri, httpProxyService: proxy.Object);
        FunctionMetadata function = await LoadFunctionAsync(worker);

        await InvokeHttpAsync(worker, function, proxy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://worker-advertised:8080")]
    [InlineData("not-an-absolute-uri")]
    public async Task InvokeAsync_WithoutLinkHttpEndpointRejectsHttpInvocation(string advertisedHttpUri)
    {
        Mock<IHttpProxyService> proxy = new(MockBehavior.Strict);
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", advertisedHttpUri: advertisedHttpUri, httpProxyService: proxy.Object);
        FunctionMetadata function = await LoadFunctionAsync(worker);

        await AssertHttpInvocationRejectedAsync(worker, function, proxy);
    }

    [Theory]
    [InlineData(CapabilitiesUpdateStrategy.Merge, null)]
    [InlineData(CapabilitiesUpdateStrategy.Merge, "not-an-absolute-uri")]
    [InlineData(CapabilitiesUpdateStrategy.Replace, null)]
    [InlineData(CapabilitiesUpdateStrategy.Replace, "http://replacement-advertised:8080")]
    public async Task InvokeAsync_CapabilityReloadCannotChangeLinkHttpEndpoint(CapabilitiesUpdateStrategy strategy, string advertisedHttpUri)
    {
        Mock<IHttpProxyService> proxy = CreateProxy();
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", httpEndpoint: HttpEndpoint, advertisedHttpUri: "http://worker-advertised:8080", httpProxyService: proxy.Object);
        await ReloadCapabilitiesAsync(worker, strategy, advertisedHttpUri);
        FunctionMetadata function = await LoadFunctionAsync(worker);

        await InvokeHttpAsync(worker, function, proxy);
    }

    [Theory]
    [InlineData(CapabilitiesUpdateStrategy.Merge)]
    [InlineData(CapabilitiesUpdateStrategy.Replace)]
    public async Task InvokeAsync_CapabilityReloadCannotEnableHttpWithoutLinkEndpoint(CapabilitiesUpdateStrategy strategy)
    {
        Mock<IHttpProxyService> proxy = new(MockBehavior.Strict);
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", httpProxyService: proxy.Object);
        await ReloadCapabilitiesAsync(worker, strategy, "http://worker-advertised:8080");
        FunctionMetadata function = await LoadFunctionAsync(worker);

        await AssertHttpInvocationRejectedAsync(worker, function, proxy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_NonHttpInvocationDoesNotUseHttpProxy(bool hasHttpEndpoint)
    {
        Mock<IHttpProxyService> proxy = new(MockBehavior.Strict);
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker", httpEndpoint: hasHttpEndpoint ? HttpEndpoint : null, httpProxyService: proxy.Object);
        FunctionMetadata function = await LoadFunctionAsync(worker, "timerTrigger");
        ScriptInvocationContext invocation = CreateInvocation(function);

        Assert.True(await worker.Channel.FunctionInputBuffers[function.GetFunctionId()].SendAsync(invocation));
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);

        Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
        proxy.VerifyNoOtherCalls();
    }

    private static Mock<IHttpProxyService> CreateProxy()
    {
        Mock<IHttpProxyService> proxy = new(MockBehavior.Strict);
        proxy.Setup(service => service.StartForwarding(It.IsAny<ScriptInvocationContext>(), HttpEndpoint));
        proxy.Setup(service => service.EnsureSuccessfulForwardingAsync(It.IsAny<ScriptInvocationContext>()))
            .Returns(Task.CompletedTask);

        return proxy;
    }

    private static async Task InvokeHttpAsync(ClientWorkerChannelTestHarness worker, FunctionMetadata function, Mock<IHttpProxyService> proxy)
    {
        ScriptInvocationContext invocation = CreateInvocation(function);
        using MemoryStream body = new(Encoding.UTF8.GetBytes("HTTP body must not be serialized over gRPC"));
        HttpRequest httpRequest = new DefaultHttpContext().Request;
        httpRequest.Method = "POST";
        httpRequest.Host = new("incoming.example");
        httpRequest.Body = body;
        httpRequest.ContentType = "text/plain";
        invocation.Inputs = [("req", DataType.String, httpRequest)];

        Assert.True(await worker.Channel.FunctionInputBuffers[function.GetFunctionId()].SendAsync(invocation));
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);

        Assert.Equal(new RpcHttp(), Assert.Single(request.InvocationRequest.InputData).Data.Http);
        proxy.Verify(service => service.StartForwarding(invocation, HttpEndpoint), Times.Once);
        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);
        Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
        proxy.Verify(service => service.EnsureSuccessfulForwardingAsync(invocation), Times.Once);
        proxy.VerifyNoOtherCalls();
    }

    private static async Task AssertHttpInvocationRejectedAsync(
        ClientWorkerChannelTestHarness worker, FunctionMetadata function, Mock<IHttpProxyService> proxy)
    {
        ScriptInvocationContext invocation = CreateInvocation(function);

        Assert.True(await worker.Channel.FunctionInputBuffers[function.GetFunctionId()].SendAsync(invocation));
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => invocation.ResultSource.Task.WaitAsync(TestTimeout));

        Assert.Contains("workerHttpEndpoint", exception.Message, StringComparison.Ordinal);
        Assert.False(worker.Transport.Requests.TryRead(out _));
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
