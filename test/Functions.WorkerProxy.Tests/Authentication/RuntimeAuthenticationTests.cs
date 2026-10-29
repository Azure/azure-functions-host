// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Azure.Functions.Tests;
using Azure.Functions.WorkerProxy.Authentication;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    public static IEnumerable<object[]> InvalidRuntimeRequests =>
        from transport in new[] { "http", "grpc" }
        from failure in new[]
        {
            "missing", "malformed", "expired", "tampered", "wrong-key", "app-key",
            "wrong-worker", "wrong-purpose", "future", "duplicate"
        }
        select new object[] { transport, failure };

    [Theory]
    [MemberData(nameof(InvalidRuntimeRequests))]
    public async Task RuntimeListeners_RejectInvalidCallerProof(string transport, string failure)
    {
        await using WorkerProxyWebApplicationFactory factory = new(useTestAuthentication: false);
        AssignForAuthentication(factory);
        string token = CallerTokenForFailure(failure);
        Metadata headers = new();
        if (!string.Equals(failure, "missing", StringComparison.Ordinal))
        {
            headers.Add(RuntimeWorkerAuthentication.WorkerTokenHeader, token);
        }

        if (string.Equals(failure, "duplicate", StringComparison.Ordinal))
        {
            headers.Add(RuntimeWorkerAuthentication.WorkerTokenHeader, token);
        }

        using CancellationTokenSource timeout = new(TestTimeout);
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
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("c2hvcnQ=")]
    public async Task RuntimeListeners_InvalidContainerKeyFailsClosed(string? key)
    {
        await using WorkerProxyWebApplicationFactory factory = new(
            new Dictionary<string, string?> { [RuntimeWorkerAuthentication.WorkerKeySetting] = key },
            useTestAuthentication: false);
        AssignForAuthentication(factory);
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient client = factory.CreateHttpForwardingClient();
        using HttpResponseMessage response = await client.GetAsync("/invoke", timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        Grpc.Core.RpcException exception = await runtime.WriteAndReadRejectionAsync(CreateMessage("rejected"), timeout.Token);
        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task RuntimeListeners_UnassignedProxyFailsClosed()
    {
        await using WorkerProxyWebApplicationFactory factory = new(useTestAuthentication: false);
        using HttpClient client = factory.CreateHttpForwardingClient();
        using HttpResponseMessage response = await client.GetAsync("/invoke");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RuntimeListeners_OnlyCallerJwtIsRequiredForHttpAndCustomerHeadersArePreserved()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using WebApplication worker = builder.Build();
        worker.Run(async context =>
        {
            Assert.Equal("Bearer customer-value", context.Request.Headers.Authorization);
            Assert.False(context.Request.Headers.ContainsKey(RuntimeWorkerAuthentication.WorkerTokenHeader));
            Assert.False(context.Request.Headers.ContainsKey(RuntimeWorkerAuthentication.AppTokenHeader));
            await context.Response.WriteAsync("authenticated");
        });
        await worker.StartAsync();
        string address = worker.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        await using WorkerProxyWebApplicationFactory factory = new(new Dictionary<string, string?>
        {
            ["WorkerProxy:WorkerHttpEndpoint"] = address,
            [RuntimeWorkerAuthentication.AppKeySetting] = null
        }, useTestAuthentication: false);
        AssignForAuthentication(factory);
        using HttpClient client = factory.CreateHttpForwardingClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "customer-value");
        client.DefaultRequestHeaders.Add(RuntimeWorkerAuthentication.AppTokenHeader, "discard-legacy-header");

        using HttpResponseMessage response = await client.GetAsync("/invoke");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("authenticated", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    public async Task ReverseProof_MissingNormalAppKeyFailsDespiteAssignmentEnvironment(string? key)
    {
        await using WorkerProxyWebApplicationFactory factory = new(
            new Dictionary<string, string?> { [RuntimeWorkerAuthentication.AppKeySetting] = key },
            useTestAuthentication: false);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = await StartAssignedWorkerAsync(factory, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.ReadAsync(timeout.Token);
        await runtime.WriteAsync(new() { WorkerInitRequest = new() }, timeout.Token);
        await worker.ReadAsync(timeout.Token);
        await worker.WriteAsync(CreateInitResponse("init", httpUri: null), timeout.Token);

        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
    }

    [Theory]
    [InlineData(StatusResult.Types.Status.Success)]
    [InlineData(StatusResult.Types.Status.Failure)]
    [InlineData(StatusResult.Types.Status.Cancelled)]
    public async Task ReverseProof_ProxyOverwritesCustomerProofAndKeepsItOutOfAssignmentStateAndCustomerMessages(StatusResult.Types.Status status)
    {
        bool initializationFails = status is not StatusResult.Types.Status.Success;
        await using WorkerProxyWebApplicationFactory factory = new(useTestAuthentication: false);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = await StartAssignedWorkerAsync(factory, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.ReadAsync(timeout.Token);
        await runtime.WriteAsync(new() { WorkerInitRequest = new() }, timeout.Token);
        StreamingMessage request = await worker.ReadAsync(timeout.Token);
        Assert.DoesNotContain(RuntimeWorkerAuthentication.AppProofCapability, request.WorkerInitRequest.Capabilities.Keys);
        StreamingMessage reply = CreateInitResponse("init", httpUri: initializationFails ? "http://localhost:1234/customer/" : null);
        if (initializationFails)
        {
            reply.WorkerInitResponse.Result = new()
            {
                Status = status,
                Exception = new() { Message = "original-worker-initialization-error", StackTrace = "original-stack" }
            };
        }
        RuntimeWorkerAuthentication.SetAppProof(reply.WorkerInitResponse.Capabilities, "customer-supplied-proof");
        await worker.WriteAsync(reply, timeout.Token);
        StreamingMessage received = await runtime.ReadAsync(timeout.Token, retainProof: true);
        string? proof = RuntimeWorkerAuthentication.RemoveAppProof(received.WorkerInitResponse.Capabilities);

        Assert.NotEqual("customer-supplied-proof", proof);
        Assert.True(RuntimeWorkerAuthentication.ValidateAppProof(proof, RuntimeAuthenticationTestData.AppKey,
            RuntimeAuthenticationTestData.AppName, RuntimeAuthenticationTestData.WorkerId, DateTimeOffset.UtcNow));
        Assert.Equal(reply.WorkerInitResponse.Result, received.WorkerInitResponse.Result);
        if (initializationFails)
        {
            Assert.Equal("http://localhost:1234/customer/", received.WorkerInitResponse.Capabilities["HttpUri"]);
            Assert.False(received.WorkerInitResponse.Capabilities.ContainsKey("FunctionGroupName"));
            Assert.Null(factory.Services.GetRequiredService<FunctionRpcRelay>().WorkerHttpDestination);
        }
        Assert.Equal("customer-supplied-proof", reply.WorkerInitResponse.Capabilities[RuntimeWorkerAuthentication.AppProofCapability]);
        using HttpClient management = factory.CreateWorkerProxyClient();
        using StringContent stateBody = new("null", Encoding.UTF8, "application/json");
        using HttpResponseMessage stateResponse = await management.PostAsync("/admin/infra/instanceState", stateBody, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        string state = await stateResponse.Content.ReadAsStringAsync(timeout.Token);
        Assert.DoesNotContain(proof!, state, StringComparison.Ordinal);
        Assert.DoesNotContain(RuntimeAuthenticationTestData.EncodedAppKey, state, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeyProvider_UsesOnlyNormalConfigurationForAppKeyAndAcceptedAssignmentForAppName()
    {
        await using WorkerProxyWebApplicationFactory factory = new(useTestAuthentication: false);
        AssignForAuthentication(factory);
        RuntimeAuthenticationKeys keys = Assert.IsType<RuntimeAuthenticationKeys>(
            factory.Services.GetRequiredService<IRuntimeAuthenticationKeyProvider>().GetKeys());

        Assert.Equal(RuntimeAuthenticationTestData.AppKey, keys.AppKey.ToArray());
        Assert.Equal(RuntimeAuthenticationTestData.AppName, keys.AppName);
        Assert.Equal(RuntimeAuthenticationTestData.WorkerId, keys.WorkerId);
        Assert.Equal(RuntimeAuthenticationTestData.EncodedAppKey,
            factory.Services.GetRequiredService<IConfiguration>()[RuntimeWorkerAuthentication.AppKeySetting]);
        factory.Services.GetRequiredService<WorkerPodStateManager>().OnSessionTerminated(1);
        Assert.Null(factory.Services.GetRequiredService<IRuntimeAuthenticationKeyProvider>().GetKeys());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuntimeListeners_RejectPlaintext(bool grpc)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient forwarding = factory.CreateHttpForwardingClient();
        Uri address = grpc ? factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime) : forwarding.BaseAddress!;
        using HttpClient client = new();
        using CancellationTokenSource timeout = new(TestTimeout);
        UriBuilder plaintext = new(address) { Scheme = Uri.UriSchemeHttp };

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri(plaintext.Uri, "/invoke"), timeout.Token));
    }

    [Fact]
    public async Task RuntimeListeners_SelfSignedCertificateIsNotTrustedByOrdinaryClients()
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient forwarding = factory.CreateHttpForwardingClient();
        using HttpClient ordinary = new();

        await Assert.ThrowsAsync<HttpRequestException>(() => ordinary.GetAsync(forwarding.BaseAddress));
    }

    [Fact]
    public void Certificate_LoadsProvisionedPemWithoutGeneratingReplacement()
    {
        using TestCertificateFiles files = new();
        var options = Options.Create(new WorkerProxyOptions { CertificatePath = files.CertificatePath, CertificateKeyPath = files.KeyPath });
        using WorkerProxyCertificate first = new(options);
        using WorkerProxyCertificate second = new(options);

        Assert.True(first.Certificate.HasPrivateKey);
        Assert.Equal(first.Certificate.Thumbprint, second.Certificate.Thumbprint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Certificate_RejectsMissingOrMalformedFiles(bool malformed)
    {
        using TestCertificateFiles files = new();
        if (malformed)
        {
            File.WriteAllText(files.KeyPath, "invalid-pem");
        }
        else
        {
            File.Delete(files.CertificatePath);
        }

        var options = Options.Create(new WorkerProxyOptions
        {
            CertificatePath = files.CertificatePath,
            CertificateKeyPath = files.KeyPath
        });
        if (malformed)
        {
            Assert.ThrowsAny<CryptographicException>(() => new WorkerProxyCertificate(options));
        }
        else
        {
            Assert.Throws<FileNotFoundException>(() => new WorkerProxyCertificate(options));
        }
    }

    [Fact]
    public void Certificate_RejectsMismatchedCertificateAndPrivateKey()
    {
        using TestCertificateFiles first = new();
        using TestCertificateFiles second = new();

        Assert.Throws<ArgumentException>(() => new WorkerProxyCertificate(Options.Create(new WorkerProxyOptions
        {
            CertificatePath = first.CertificatePath,
            CertificateKeyPath = second.KeyPath
        })));
    }

    private static void AssignForAuthentication(WorkerProxyWebApplicationFactory factory)
    {
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "local-worker");
        AssignAuthenticationState(manager);
    }

    private static void AssignAuthenticationState(WorkerPodStateManager manager)
        => Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(new(
            WorkerStartupMode.Preconfigured, RuntimeAuthenticationTestData.AppName, "group", false,
            new Dictionary<string, string>
            {
                [RuntimeWorkerAuthentication.AppKeySetting] =
                    Convert.ToHexString(SHA256.HashData("assignment-is-not-the-proof-key-source"u8))
            }, string.Empty)));

    private static async Task<RelayClient> StartAssignedWorkerAsync(WorkerProxyWebApplicationFactory factory, CancellationToken token)
    {
        RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, token);
        await worker.WriteAsync(CreateStartStream(), token);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        while (!manager.State.IsWorkerReady)
        {
            await Task.Delay(10, token);
        }

        AssignAuthenticationState(manager);

        return worker;
    }

    private static string CallerTokenForFailure(string failure)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string audience = RuntimeWorkerAuthentication.WorkerAudience(RuntimeAuthenticationTestData.WorkerId);
        string purpose = RuntimeWorkerAuthentication.WorkerPurpose;
        byte[] key = RuntimeAuthenticationTestData.WorkerKey;

        return failure switch
        {
            "malformed" => "invalid-token",
            "expired" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now.AddMinutes(-6)),
            "future" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now.AddMinutes(1)),
            "wrong-key" => RuntimeWorkerAuthentication.CreateToken(new byte[32], audience, purpose, now),
            "app-key" => RuntimeWorkerAuthentication.CreateToken(RuntimeAuthenticationTestData.AppKey, audience, purpose, now),
            "wrong-worker" => RuntimeWorkerAuthentication.CreateToken(key, audience + "-other", purpose, now),
            "wrong-purpose" => RuntimeWorkerAuthentication.CreateToken(key, audience, "other-purpose", now),
            "tampered" => RuntimeAuthenticationTestData.CreateCallerToken() + "A",
            _ => RuntimeAuthenticationTestData.CreateCallerToken()
        };
    }
}
