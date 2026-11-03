// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
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
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Theory]
    [InlineData("grpc", "missing")]
    [InlineData("grpc", "wrong-key")]
    [InlineData("http", "tampered")]
    [InlineData("http", "future")]
    [InlineData("http", "duplicate")]
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
        IRuntimeAuthenticationKeyProvider provider = factory.Services.GetRequiredService<IRuntimeAuthenticationKeyProvider>();
        RuntimeWorkerAuthenticationKeys workerKeys = Assert.IsType<RuntimeWorkerAuthenticationKeys>(provider.GetWorkerKeys());
        RuntimeAppAuthenticationKeys appKeys = Assert.IsType<RuntimeAppAuthenticationKeys>(provider.GetAppKeys());

        Assert.Equal(RuntimeAuthenticationTestData.AppKey, appKeys.AppKey.ToArray());
        Assert.Equal(RuntimeAuthenticationTestData.AppName, appKeys.AppName);
        Assert.Equal(RuntimeAuthenticationTestData.WorkerId, appKeys.WorkerId);
        ContainerManagementAuthenticationHandler.Credentials container = factory.Services
            .GetRequiredService<IOptionsMonitor<ContainerManagementAuthenticationHandler.Credentials>>()
            .Get(ContainerManagementAuthenticationHandler.SchemeName);
        Assert.True(workerKeys.WorkerKey.Equals(new ReadOnlyMemory<byte>(container.Key)));
        Assert.Equal(container.PodName, workerKeys.WorkerId);
        Assert.Equal(RuntimeAuthenticationTestData.EncodedAppKey,
            factory.Services.GetRequiredService<IConfiguration>()[RuntimeWorkerAuthentication.AppKeySetting]);
        IConfiguration configuration = factory.Services.GetRequiredService<IConfiguration>();
        byte[] replacement = SHA256.HashData("updated-normal-configuration-key"u8);
        configuration[RuntimeWorkerAuthentication.AppKeySetting] = Convert.ToHexString(replacement);
        Assert.Equal(replacement, Assert.IsType<RuntimeAppAuthenticationKeys>(provider.GetAppKeys()).AppKey.ToArray());
        factory.Services.GetRequiredService<WorkerPodStateManager>().OnSessionTerminated(1);
        Assert.Null(provider.GetWorkerKeys());
        Assert.Null(provider.GetAppKeys());
    }

    [Fact]
    public void CallerAuthentication_DoesNotReadAppKeyAfterStartup()
    {
        Mock<IConfiguration> configuration = new(MockBehavior.Strict);
        configuration.SetupGet(value => value[RuntimeWorkerAuthentication.AppKeySetting])
            .Returns(RuntimeAuthenticationTestData.EncodedAppKey);
        WorkerPodStateManager manager = new(
            Options.Create(new WorkerProxyOptions { PodName = RuntimeAuthenticationTestData.WorkerId }), TimeProvider.System);
        RuntimeAuthenticationKeyProvider provider = CreateAuthenticationKeyProvider(configuration.Object, manager);
        AssignAuthenticationState(manager);
        RuntimeRequestAuthenticator authenticator = new(provider, TimeProvider.System, NullLogger<RuntimeRequestAuthenticator>.Instance);
        configuration.Invocations.Clear();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            DefaultHttpContext context = new();
            context.Request.Scheme = Uri.UriSchemeHttps;
            context.Request.Headers[RuntimeWorkerAuthentication.WorkerTokenHeader] = RuntimeAuthenticationTestData.CreateCallerToken();

            Assert.True(authenticator.TryAuthenticate(context));
            Assert.False(context.Request.Headers.ContainsKey(RuntimeWorkerAuthentication.WorkerTokenHeader));
        }

        configuration.VerifyGet(value => value[RuntimeWorkerAuthentication.AppKeySetting], Times.Never);
        configuration.VerifyNoOtherCalls();
    }

    [Fact]
    public void AppProof_ReadsLiveKeyAndRefreshesConfigurationHealthWithoutChangingAssignment()
    {
        string? encodedKey = null;
        Mock<IConfiguration> configuration = new(MockBehavior.Strict);
        configuration.SetupGet(value => value[RuntimeWorkerAuthentication.AppKeySetting]).Returns(() => encodedKey);
        WorkerPodStateManager manager = new(
            Options.Create(new WorkerProxyOptions { PodName = RuntimeAuthenticationTestData.WorkerId }), TimeProvider.System);
        RuntimeAuthenticationKeyProvider provider = CreateAuthenticationKeyProvider(configuration.Object, manager);
        AssignAuthenticationState(manager);
        RuntimeRequestAuthenticator authenticator = new(provider, TimeProvider.System, NullLogger<RuntimeRequestAuthenticator>.Instance);
        Assert.True(manager.State.HasAuthenticationConfigurationError);
        Assert.True(manager.State.IsUnhealthy);

        encodedKey = RuntimeAuthenticationTestData.EncodedAppKey;
        string original = authenticator.CreateAppProof();

        Assert.True(RuntimeWorkerAuthentication.ValidateAppProof(original, RuntimeAuthenticationTestData.AppKey,
            RuntimeAuthenticationTestData.AppName, RuntimeAuthenticationTestData.WorkerId, DateTimeOffset.UtcNow));
        Assert.False(manager.State.HasAuthenticationConfigurationError);
        Assert.False(manager.State.IsUnhealthy);
        byte[] replacement = SHA256.HashData("replacement-live-proof-key"u8);
        encodedKey = Convert.ToHexString(replacement);
        string updated = authenticator.CreateAppProof();

        Assert.True(RuntimeWorkerAuthentication.ValidateAppProof(updated, replacement,
            RuntimeAuthenticationTestData.AppName, RuntimeAuthenticationTestData.WorkerId, DateTimeOffset.UtcNow));
        Assert.False(RuntimeWorkerAuthentication.ValidateAppProof(updated, RuntimeAuthenticationTestData.AppKey,
            RuntimeAuthenticationTestData.AppName, RuntimeAuthenticationTestData.WorkerId, DateTimeOffset.UtcNow));
        encodedKey = "invalid";
        Assert.Throws<InvalidOperationException>(authenticator.CreateAppProof);
        Assert.True(manager.State.HasAuthenticationConfigurationError);
        Assert.True(manager.State.IsUnhealthy);
        Assert.Equal(WorkerAssignmentState.Ready, manager.State.AssignmentState);
        Assert.False(manager.State.IsWorkerAttached);
        Assert.NotNull(provider.GetWorkerKeys());
        configuration.VerifyGet(value => value[RuntimeWorkerAuthentication.AppKeySetting], Times.Exactly(4));
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
    public async Task Certificate_LoadsProvisionedPemWithoutGeneratingReplacement()
    {
        using TestCertificateFiles files = new();
        string certificate = File.ReadAllText(files.CertificatePath);
        string key = File.ReadAllText(files.KeyPath);
        await using WorkerProxyWebApplicationFactory factory = new(new Dictionary<string, string?>
        {
            ["WorkerProxy:CertificatePath"] = files.CertificatePath,
            ["WorkerProxy:CertificateKeyPath"] = files.KeyPath
        });
        using HttpClient http = factory.CreateHttpForwardingClient();
        KestrelServerOptions options = factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        Assert.Equal(files.CertificatePath, options.ConfigurationLoader!.Configuration["Certificates:Default:Path"]);
        Assert.Equal(files.KeyPath, options.ConfigurationLoader.Configuration["Certificates:Default:KeyPath"]);
        foreach (Uri address in new[] { http.BaseAddress!, factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime) })
        {
            using CancellationTokenSource timeout = new(TestTimeout);
            using TcpClient connection = new();
            await connection.ConnectAsync(address.Host, address.Port, timeout.Token);
            using SslStream tls = new(connection.GetStream());
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = address.Host,
                RemoteCertificateValidationCallback = static (_, _, _, _) => true
            }, timeout.Token);
            Assert.Equal(files.Certificate.Thumbprint, tls.RemoteCertificate!.GetCertHashString());
        }
        Assert.Equal(certificate, File.ReadAllText(files.CertificatePath));
        Assert.Equal(key, File.ReadAllText(files.KeyPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Certificate_RejectsMissingOrMalformedFiles(bool malformed)
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

        await using WorkerProxyWebApplicationFactory factory = new(new Dictionary<string, string?>
        {
            ["WorkerProxy:CertificatePath"] = files.CertificatePath,
            ["WorkerProxy:CertificateKeyPath"] = files.KeyPath
        });
        if (malformed)
        {
            Assert.Throws<ArgumentException>(() => factory.CreateHttpForwardingClient());
        }
        else
        {
            Assert.Throws<FileNotFoundException>(() => factory.CreateHttpForwardingClient());
        }
    }

    [Fact]
    public async Task Certificate_RejectsMismatchedCertificateAndPrivateKey()
    {
        using TestCertificateFiles first = new();
        using TestCertificateFiles second = new();

        await using WorkerProxyWebApplicationFactory factory = new(new Dictionary<string, string?>
        {
            ["WorkerProxy:CertificatePath"] = first.CertificatePath,
            ["WorkerProxy:CertificateKeyPath"] = second.KeyPath
        });
        Assert.Throws<InvalidOperationException>(() => factory.CreateHttpForwardingClient());
    }

    private static void AssignForAuthentication(WorkerProxyWebApplicationFactory factory)
    {
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "local-worker");
        AssignAuthenticationState(manager);
    }

    private static RuntimeAuthenticationKeyProvider CreateAuthenticationKeyProvider(
        IConfiguration configuration, WorkerPodStateManager manager)
    {
        IConfiguration containerConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WEBSITE_POD_NAME"] = RuntimeAuthenticationTestData.WorkerId,
            [RuntimeWorkerAuthentication.WorkerKeySetting] = RuntimeAuthenticationTestData.EncodedWorkerKey
        }).Build();
        ContainerManagementAuthenticationHandler.Credentials container = new();
        container.Configure(containerConfiguration, NullLogger<ContainerManagementAuthenticationHandler.Credentials>.Instance);
        Mock<IOptionsMonitor<ContainerManagementAuthenticationHandler.Credentials>> options = new();
        options.Setup(value => value.Get(ContainerManagementAuthenticationHandler.SchemeName)).Returns(container);

        return new(configuration, options.Object, manager, NullLogger<RuntimeAuthenticationKeyProvider>.Instance);
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
            "future" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now.AddMinutes(1)),
            "wrong-key" => RuntimeWorkerAuthentication.CreateToken(new byte[32], audience, purpose, now),
            "tampered" => RuntimeAuthenticationTestData.CreateCallerToken() + "A",
            _ => RuntimeAuthenticationTestData.CreateCallerToken()
        };
    }
}
