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
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Fact]
    public async Task Relay_ProxyBootstrapPrecedesCustomerAndRuntimeLossRequestsRemoval()
    {
        const string localSecret = "synthetic-pod-identity-secret";
        await using WorkerProxyWebApplicationFactory factory = new(new Dictionary<string, string?>
        {
            ["WorkerProxy:TokenServiceProxy:Enabled"] = "true",
            ["WorkerProxy:TokenServiceProxy:Port"] = "0"
        }, useTestAuthentication: false);
        using CancellationTokenSource timeout = new(TestTimeout);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(new(
            WorkerStartupMode.Preconfigured, RuntimeAuthenticationTestData.AppName, "group", false,
            new Dictionary<string, string> { ["IDENTITY_HEADER"] = localSecret }, string.Empty)));
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
        await firstRuntime.CompleteRequestAsync(timeout.Token);
        Assert.Equal(StatusCode.Unavailable, await firstRuntime.WaitForTerminationAsync(timeout.Token));
        Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
        using HttpResponseMessage abandoned = await oldRequest.WaitAsync(timeout.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, abandoned.StatusCode);
        WorkerPodState removed = manager.State;
        Assert.True(removed.RemovalRequested);
        Assert.False(removed.IsWorkerReady);
        Assert.Equal(assigned.StartupTime, removed.StartupTime);

        await using RelayClient replacement = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        string replacementSession = Guid.NewGuid().ToString("N");
        Grpc.Core.RpcException rejected = await replacement.WriteAndReadRejectionAsync(new()
        {
            HttpProxy = new() { SessionId = replacementSession, Hello = new() { Version = TokenServiceProxyProtocol.Version, Limits = limits.ToLimits() } }
        }, timeout.Token);
        Assert.Equal(StatusCode.Unauthenticated, rejected.StatusCode);
        using HttpClient http = factory.CreateHttpForwardingClient();
        using HttpResponseMessage invocation = await http.GetAsync("/after-removal", timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, invocation.StatusCode);
        using HttpResponseMessage freshRequest = await identity.GetAsync("/msi/token?resource=new", timeout.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, freshRequest.StatusCode);
        using HttpClient management = factory.CreateWorkerProxyClient();
        WorkerOriginState checkpoint = await ReadManagementStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.RemovalRequested, checkpoint.Status);
        Assert.Equal(WorkerLinkState.Unlinked, checkpoint.Link.State);
        Assert.Same(removed, manager.State);
    }
}
