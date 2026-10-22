// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Azure.Functions.WorkerProxy.Authentication;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    public static IEnumerable<object[]> InvalidRuntimeRequests =>
        from transport in new[] { "http", "grpc" }
        from failure in new[]
        {
            "missing-both", "missing-worker", "missing-app", "malformed-worker", "malformed-app",
            "expired-worker", "expired-app", "tampered-worker", "tampered-app",
            "wrong-worker-key", "wrong-app-key", "wrong-worker-audience", "wrong-app-audience",
            "wrong-worker-purpose", "wrong-app-purpose", "future-worker", "future-app",
            "swapped", "duplicate-worker", "duplicate-app"
        }
        select new object[] { transport, failure };

    [Theory]
    [MemberData(nameof(InvalidRuntimeRequests))]
    public async Task RuntimeListeners_RejectInvalidCredentialsBeforeAdmission(string transport, string failure)
    {
        await using WorkerProxyWebApplicationFactory factory = new(useTestAuthentication: false);
        AssignForAuthentication(factory);
        (string workerToken, string appToken) = TokensForFailure(failure);
        using CancellationTokenSource timeout = new(TestTimeout);
        Metadata headers = new();
        if (!string.Equals(failure, "missing-both", StringComparison.Ordinal)
            && !string.Equals(failure, "missing-worker", StringComparison.Ordinal))
        {
            headers.Add(RuntimeWorkerAuthentication.WorkerTokenHeader, workerToken);
        }

        if (!string.Equals(failure, "missing-both", StringComparison.Ordinal)
            && !string.Equals(failure, "missing-app", StringComparison.Ordinal))
        {
            headers.Add(RuntimeWorkerAuthentication.AppTokenHeader, appToken);
        }

        if (string.Equals(failure, "duplicate-worker", StringComparison.Ordinal))
        {
            headers.Add(RuntimeWorkerAuthentication.WorkerTokenHeader, workerToken);
        }

        if (string.Equals(failure, "duplicate-app", StringComparison.Ordinal))
        {
            headers.Add(RuntimeWorkerAuthentication.AppTokenHeader, appToken);
        }

        if (string.Equals(transport, "grpc", StringComparison.Ordinal))
        {
            await using RelayClient runtime = CreateClient(
                factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime), timeout.Token, headers);
            Grpc.Core.RpcException exception = await runtime.WriteAndReadRejectionAsync(CreateMessage("rejected"), timeout.Token);
            Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
            Assert.False(factory.Services.GetRequiredService<FunctionRpcRelay>().IsAttached(FunctionRpcRelaySide.Runtime));
        }
        else
        {
            using HttpClient client = factory.CreateHttpForwardingClient(authenticate: false);
            using HttpRequestMessage request = new(HttpMethod.Post, "/invoke");
            foreach (Metadata.Entry header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using HttpResponseMessage response = await client.SendAsync(request, timeout.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync(timeout.Token));
        }
    }

    [Theory]
    [InlineData("unassigned")]
    [InlineData("missing-assignment-key")]
    [InlineData("invalid-assignment-key")]
    [InlineData("missing-worker-key")]
    [InlineData("invalid-worker-key")]
    [InlineData("weak-worker-key")]
    public async Task RuntimeListeners_MissingPrivateAuthenticationStateFailsClosed(string failure)
    {
        Dictionary<string, string?> configuration = new()
        {
            // A process/configuration app key must not substitute for an accepted private assignment.
            [RuntimeWorkerAuthentication.AppKeySetting] = RuntimeAuthenticationTestData.EncodedAppKey
        };
        if (failure.EndsWith("worker-key", StringComparison.Ordinal))
        {
            configuration[RuntimeWorkerAuthentication.WorkerKeySetting] = failure switch
            {
                "missing-worker-key" => null,
                "weak-worker-key" => Convert.ToBase64String(new byte[16]),
                _ => "invalid-key"
            };
        }

        await using WorkerProxyWebApplicationFactory factory = new(configuration, useTestAuthentication: false);
        if (!string.Equals(failure, "unassigned", StringComparison.Ordinal))
        {
            string? appKey = failure switch
            {
                "missing-assignment-key" => null,
                "invalid-assignment-key" => "invalid-key",
                _ => RuntimeAuthenticationTestData.EncodedAppKey
            };
            AssignForAuthentication(factory, appKey, includeAppKey: appKey is not null);
        }

        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient client = factory.CreateHttpForwardingClient();
        using HttpResponseMessage response = await client.GetAsync("/invoke", timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        Grpc.Core.RpcException exception = await runtime.WriteAndReadRejectionAsync(CreateMessage("rejected"), timeout.Token);
        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task RuntimeListeners_ValidTokensAllowTlsHttpAndGrpcAndPreserveCustomerAuthorization()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using WebApplication worker = builder.Build();
        worker.Run(async context =>
        {
            Assert.Equal("Bearer customer-token", context.Request.Headers.Authorization);
            Assert.False(context.Request.Headers.ContainsKey(RuntimeWorkerAuthentication.WorkerTokenHeader));
            Assert.False(context.Request.Headers.ContainsKey(RuntimeWorkerAuthentication.AppTokenHeader));
            await context.Response.WriteAsync("authenticated");
        });
        await worker.StartAsync();
        string workerAddress = worker.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        await using WorkerProxyWebApplicationFactory factory = new(
            new Dictionary<string, string?> { ["WorkerProxy:WorkerHttpEndpoint"] = workerAddress },
            useTestAuthentication: false);
        AssignForAuthentication(factory);
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient client = factory.CreateHttpForwardingClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "customer-token");

        using HttpResponseMessage response = await client.GetAsync("/invoke", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("authenticated", await response.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(Uri.UriSchemeHttps, client.BaseAddress!.Scheme);

        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.WriteAsync(CreateMessage("authenticated"), timeout.Token);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        Assert.True(relay.IsAttached(FunctionRpcRelaySide.Runtime));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuntimeListeners_RejectPlaintext(bool grpc)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient forwardingClient = factory.CreateHttpForwardingClient();
        Uri address = grpc ? factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime)
            : forwardingClient.BaseAddress!;
        using HttpClient client = new();
        using CancellationTokenSource timeout = new(TestTimeout);
        UriBuilder plaintext = new(address) { Scheme = Uri.UriSchemeHttp };
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(new Uri(plaintext.Uri, "/invoke"), timeout.Token));
    }

    [Fact]
    public async Task RuntimeListeners_SelfSignedCertificateIsNotTrustedByOrdinaryClients()
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient scopedClient = factory.CreateHttpForwardingClient();
        using HttpClient ordinaryClient = new();

        await Assert.ThrowsAsync<HttpRequestException>(() => ordinaryClient.GetAsync(scopedClient.BaseAddress));
    }

    [Fact]
    public void Certificate_IsUniqueForEachProxyLifetime()
    {
        using WorkerProxyCertificate first = new();
        using WorkerProxyCertificate second = new();

        Assert.True(first.Certificate.HasPrivateKey);
        Assert.NotEqual(first.Certificate.Thumbprint, second.Certificate.Thumbprint);
        Assert.Equal(first.Certificate.Subject, first.Certificate.Issuer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-key")]
    public async Task Assignment_RejectsMissingOrInvalidAppKeyWithoutPublishingIt(string? appKey)
    {
        await using WorkerProxyWebApplicationFactory factory = new(useTestAuthentication: false);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "local-worker");
        WorkerAssignRequest request = new()
        {
            StartupMode = nameof(WorkerStartupMode.Preconfigured),
            FunctionAppName = RuntimeAuthenticationTestData.AppName,
            FunctionGroupName = "group",
            FunctionAppDirectory = string.Empty,
            IsAlwaysReady = false,
            Environment = appKey is null ? [] : new() { [RuntimeWorkerAuthentication.AppKeySetting] = appKey }
        };

        Assert.False(WorkerAssignRequestValidator.TryCreateAssignment(request, out _, out var errors));
        Assert.Equal($"environment.{RuntimeWorkerAuthentication.AppKeySetting}", Assert.Single(errors).Target);
        Assert.Equal(WorkerAssignmentState.Unassigned, manager.State.AssignmentState);
    }

    [Fact]
    public async Task KeyProvider_UsesPrivateAcceptedAssignmentAndWithdrawsItAfterTermination()
    {
        await using WorkerProxyWebApplicationFactory factory = new(useTestAuthentication: false);
        IRuntimeAuthenticationKeyProvider provider = factory.Services.GetRequiredService<IRuntimeAuthenticationKeyProvider>();
        Assert.Null(provider.GetKeys());
        AssignForAuthentication(factory);

        RuntimeAuthenticationKeys keys = Assert.IsType<RuntimeAuthenticationKeys>(provider.GetKeys());
        Assert.Equal(RuntimeAuthenticationTestData.AppKey, keys.AppKey.ToArray());
        Assert.Equal(RuntimeAuthenticationTestData.AppName, keys.AppName);
        Assert.Equal(RuntimeAuthenticationTestData.WorkerId, keys.WorkerId);
        Assert.Null(factory.Services.GetRequiredService<IConfiguration>()[RuntimeWorkerAuthentication.AppKeySetting]);

        factory.Services.GetRequiredService<WorkerPodStateManager>().OnSessionTerminated(1);
        Assert.Null(provider.GetKeys());
    }

    private static void AssignForAuthentication(
        WorkerProxyWebApplicationFactory factory, string? appKey = null, bool includeAppKey = true)
    {
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "local-worker");
        Dictionary<string, string> environment = [];
        if (includeAppKey)
        {
            environment[RuntimeWorkerAuthentication.AppKeySetting] = appKey ?? RuntimeAuthenticationTestData.EncodedAppKey;
        }

        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(new(
            WorkerStartupMode.Preconfigured, RuntimeAuthenticationTestData.AppName, "group", false, environment, string.Empty)));
    }

    private static (string WorkerToken, string AppToken) TokensForFailure(string failure)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (string workerToken, string appToken) = RuntimeAuthenticationTestData.CreateTokens(now);
        bool workerFailure = failure.EndsWith("-worker", StringComparison.Ordinal)
            || failure.StartsWith("wrong-worker", StringComparison.Ordinal);
        string audience = workerFailure ? RuntimeWorkerAuthentication.WorkerAudience(RuntimeAuthenticationTestData.WorkerId)
            : RuntimeWorkerAuthentication.AppAudience(RuntimeAuthenticationTestData.AppName);
        string purpose = workerFailure ? RuntimeWorkerAuthentication.WorkerPurpose : RuntimeWorkerAuthentication.AppPurpose;
        byte[] key = workerFailure ? RuntimeAuthenticationTestData.WorkerKey : RuntimeAuthenticationTestData.AppKey;

        string invalid = failure switch
        {
            "malformed-worker" or "malformed-app" => "not-a-jwt",
            "expired-worker" or "expired-app" =>
                RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now.AddMinutes(-6)),
            "future-worker" or "future-app" =>
                RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now.AddMinutes(1)),
            "wrong-worker-key" or "wrong-app-key" =>
                RuntimeWorkerAuthentication.CreateToken(new byte[32], audience, purpose, now),
            "wrong-worker-audience" or "wrong-app-audience" =>
                RuntimeWorkerAuthentication.CreateToken(key, audience + "-different-identity", purpose, now),
            "wrong-worker-purpose" or "wrong-app-purpose" =>
                RuntimeWorkerAuthentication.CreateToken(key, audience, "different-purpose", now),
            "tampered-worker" or "tampered-app" => (workerFailure ? workerToken : appToken) + "A",
            _ => workerFailure ? workerToken : appToken
        };
        if (string.Equals(failure, "swapped", StringComparison.Ordinal))
        {
            return (appToken, workerToken);
        }

        return workerFailure ? (invalid, appToken) : (workerToken, invalid);
    }
}
