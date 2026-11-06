// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Identity;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Fact]
    public async Task Relay_ProxyBootstrapPrecedesCustomerAndRuntimeRetryDoesNotReplayOldHttpRequests()
    {
        const string localSecret = "synthetic-pod-identity-secret";
        await using WorkerProxyWebApplicationFactory factory = new(new Dictionary<string, string?>
        {
            ["WorkerProxy:TokenServiceProxy:Enabled"] = "true",
            ["WorkerProxy:TokenServiceProxy:Port"] = "0",
            ["IDENTITY_HEADER"] = localSecret
        }, useTestAuthentication: false);
        using CancellationTokenSource timeout = new(TestTimeout);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        AssignAuthenticationState(manager);
        TokenServiceProxyOptions limits = new();
        await using RelayClient firstRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        string firstSession = Guid.NewGuid().ToString("N");
        await firstRuntime.WriteAsync(new()
        {
            HttpProxy = new() { SessionId = firstSession, Hello = new() { Version = TokenServiceProxyProtocol.Version, Limits = limits.ToLimits() } }
        }, timeout.Token);
        StreamingMessage initialReady = await firstRuntime.ReadAsync(timeout.Token);
        Assert.NotNull(initialReady.HttpProxy.Ready);
        Assert.False(manager.State.IsWorkerAttached);

        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        StreamingMessage start = CreateStartStream();
        await worker.WriteAsync(start, timeout.Token);
        Assert.Equal(start, await firstRuntime.ReadAsync(timeout.Token));
        WorkerPodState assigned = manager.State;
        using HttpClient identity = new()
        {
            BaseAddress = factory.Services.GetRequiredService<WorkerProxyEndpointConfiguration>().GetTokenServiceProxyAddress()
        };
        identity.DefaultRequestHeaders.Add(TokenServiceProxyProtocol.IdentityHeader, localSecret);
        Task<HttpResponseMessage> oldRequest = identity.GetAsync("/msi/token?resource=old", timeout.Token);
        StreamingMessage forwarded = await firstRuntime.ReadAsync(timeout.Token);
        Assert.NotNull(forwarded.HttpProxy.Request);
        string oldRequestId = forwarded.HttpProxy.RequestId;

        await firstRuntime.CompleteRequestAsync(timeout.Token);
        Assert.Equal(StatusCode.Unavailable, await firstRuntime.WaitForTerminationAsync(timeout.Token));
        using HttpResponseMessage abandoned = await oldRequest.WaitAsync(timeout.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, abandoned.StatusCode);
        Assert.Same(assigned, manager.State);
        Assert.True(manager.State.IsWorkerReady);

        await using RelayClient replacement = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        string replacementSession = Guid.NewGuid().ToString("N");
        await replacement.WriteAsync(new()
        {
            HttpProxy = new() { SessionId = replacementSession, Hello = new() { Version = TokenServiceProxyProtocol.Version, Limits = limits.ToLimits() } }
        }, timeout.Token);
        StreamingMessage first = await replacement.ReadAsync(timeout.Token);
        StreamingMessage second = await replacement.ReadAsync(timeout.Token);
        Assert.Contains(new[] { first, second }, message => message.StartStream is not null);
        Assert.Contains(new[] { first, second }, message => message.HttpProxy?.Ready is not null);
        Task<HttpResponseMessage> freshRequest = identity.GetAsync("/msi/token?resource=new", timeout.Token);
        StreamingMessage fresh = await replacement.ReadAsync(timeout.Token);
        Assert.Equal(replacementSession, fresh.HttpProxy.SessionId);
        Assert.NotEqual(oldRequestId, fresh.HttpProxy.RequestId);
        Assert.Equal("/msi/token?resource=new", fresh.HttpProxy.Request.Target);
        await replacement.WriteAsync(new()
        {
            HttpProxy = new()
            {
                SessionId = replacementSession,
                RequestId = fresh.HttpProxy.RequestId,
                Response = new() { StatusCode = 200, Body = ByteString.CopyFromUtf8("opaque-synthetic-token") }
            }
        }, timeout.Token);
        using HttpResponseMessage recovered = await freshRequest.WaitAsync(timeout.Token);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal("opaque-synthetic-token", await recovered.Content.ReadAsStringAsync(timeout.Token));
        Assert.Same(assigned, manager.State);

        await replacement.WriteAsync(new() { WorkerInitRequest = new() }, timeout.Token);
        Assert.NotNull((await worker.ReadAsync(timeout.Token)).WorkerInitRequest);
        await worker.WriteAsync(CreateInitResponse("init-after-bootstrap-retry", httpUri: null), timeout.Token);
        Assert.NotNull((await replacement.ReadAsync(timeout.Token)).WorkerInitResponse);
    }
}
