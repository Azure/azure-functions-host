// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

extern alias WorkerProxy;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Functions.Host.Identity;
using Azure.Functions.Rpc.Client;
using Azure.Identity;
using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using ProxyApplication = WorkerProxy::Azure.Functions.WorkerProxy.WorkerProxyApplication;
using ProxyEndpoints = WorkerProxy::Azure.Functions.WorkerProxy.WorkerProxyEndpointConfiguration;
using ProxyIdentityService = WorkerProxy::Azure.Functions.WorkerProxy.Identity.IdentityProxyService;
using ProxySide = WorkerProxy::Azure.Functions.WorkerProxy.Rpc.FunctionRpcRelaySide;

namespace Azure.Functions.Host.Tests.Identity;

[Collection(nameof(EnvironmentVariableCollection))]
public sealed class TokenProxyIntegrationTests
{
    private const string LocalSecret = "worker-local-secret";
    private const string HostSecret = "host-only-secret";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ManagedIdentityCredential_AcquiresTokenBeforeStartStream_ThenWorkerInitializesNormally()
    {
        await using Harness harness = await Harness.CreateAsync((request, _) =>
        {
            Assert.Contains("api-version=2019-08-01", request.RequestUri!.Query, StringComparison.Ordinal);
            Assert.Contains("resource=", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Equal(HostSecret, Assert.Single(request.Headers.GetValues(HttpProxyProtocol.IdentityHeader)));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                    {"access_token":"opaque-test-token","expires_on":"{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}","resource":"https://vault.azure.net","token_type":"Bearer","client_id":"00000000-0000-0000-0000-000000000001"}
                    """, Encoding.UTF8, "application/json")
            });
        });
        using CancellationTokenSource timeout = new(TestTimeout);
        using EnvironmentVariableScope endpoint = new("IDENTITY_ENDPOINT", new Uri(harness.LocalClient.BaseAddress!, "/msi/token").ToString());
        using EnvironmentVariableScope localSecret = new("IDENTITY_HEADER", LocalSecret);
        using EnvironmentVariableScope serviceFabric = new("IDENTITY_SERVER_THUMBPRINT", null);
        await harness.ConnectAsync(timeout.Token);

        AccessToken token = await new ManagedIdentityCredential().GetTokenAsync(
            new TokenRequestContext(["https://vault.azure.net/.default"]), timeout.Token);

        Assert.Equal("opaque-test-token", token.Token);
        Assert.Empty(harness.Registry.GetInitializedChannels());
        await harness.CompleteWorkerHandshakeAsync(timeout.Token);
    }

    [Fact]
    public async Task Request_BeforeEitherRpcPeer_WaitsAndForwardsWithoutStartStream()
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        string? upstreamTarget = null;
        string? upstreamSecret = null;
        await using Harness harness = await Harness.CreateAsync(async (request, cancellationToken) =>
        {
            upstreamTarget = request.RequestUri!.OriginalString;
            upstreamSecret = Assert.Single(request.Headers.GetValues(HttpProxyProtocol.IdentityHeader));
            Assert.False(request.Headers.Contains("Authorization"));
            Assert.Equal(new byte[] { 0, 255, 1 }, await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            HttpResponseMessage response = new(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("opaque upstream error")
            };
            response.Headers.TryAddWithoutValidation("Retry-After", "7");
            response.Headers.TryAddWithoutValidation("X-Repeated", new[] { "first", "second" });
            response.Headers.TryAddWithoutValidation(HttpProxyProtocol.IdentityHeader, HostSecret);
            return response;
        });
        using HttpRequestMessage local = harness.Request(HttpMethod.Post, "/MSI/token?a=%2f&a=2&resource=https%3A%2F%2Fvault.azure.net");
        local.Content = new ByteArrayContent([0, 255, 1]);
        local.Headers.TryAddWithoutValidation("Authorization", "customer-override");
        Task<HttpResponseMessage> pending = harness.LocalClient.SendAsync(local, timeout.Token);
        Assert.False(pending.IsCompleted);

        await harness.ConnectAsync(timeout.Token);
        using HttpResponseMessage result = await pending.WaitAsync(timeout.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.Equal("opaque upstream error", await result.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal("7", Assert.Single(result.Headers.GetValues("Retry-After")));
        Assert.Equal(new[] { "first", "second" }, result.Headers.GetValues("X-Repeated"));
        Assert.False(result.Headers.Contains(HttpProxyProtocol.IdentityHeader));
        Assert.Equal("http://token.test/MSI/token?a=%2f&a=2&resource=https%3A%2F%2Fvault.azure.net", upstreamTarget);
        Assert.Equal(HostSecret, upstreamSecret);
        Assert.Empty(harness.Registry.GetInitializedChannels());
    }

    [Theory]
    [InlineData(null, "/MSI/token", HttpStatusCode.Forbidden)]
    [InlineData("wrong", "/MSI/token", HttpStatusCode.Forbidden)]
    [InlineData(LocalSecret, "/api/specialize", HttpStatusCode.NotFound)]
    [InlineData(LocalSecret, "/admin/worker/assignment", HttpStatusCode.NotFound)]
    public async Task InvalidLocalRequest_DoesNotReachTokenService(string? secret, string path, HttpStatusCode expected)
    {
        int requests = 0;
        await using Harness harness = await Harness.CreateAsync((_, _) =>
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        if (secret is not null)
        {
            request.Headers.Add(HttpProxyProtocol.IdentityHeader, secret);
        }

        using HttpResponseMessage response = await harness.LocalClient.SendAsync(request, timeout.Token);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, requests);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSpecializedConfiguration_Returns503_AndTransportCanRecover()
    {
        int requests = 0;
        await using Harness harness = await Harness.CreateAsync((_, _) =>
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("token") });
        });
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);
        harness.Configuration["MSI_SECRET"] = string.Empty;

        using HttpResponseMessage invalid = await harness.SendAsync(timeout.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, invalid.StatusCode);
        Assert.Equal(0, requests);

        harness.Configuration["MSI_SECRET"] = HostSecret;
        using HttpResponseMessage recovered = await harness.SendAsync(timeout.Token);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task HostEndpointWithoutExplicitPath_ForwardsOnlyConfiguredRootPath()
    {
        await using Harness harness = await Harness.CreateAsync((request, _) =>
        {
            Assert.Equal("http://token.test/?a=%2f&a=2", request.RequestUri!.OriginalString);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("token") });
        }, "--WorkerProxy:IdentityProxy:TokenPath", "/");
        harness.Configuration["MSI_ENDPOINT"] = "http://token.test";
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);
        using HttpRequestMessage request = harness.Request(HttpMethod.Get, "/?a=%2f&a=2");
        using HttpResponseMessage response = await harness.LocalClient.SendAsync(request, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using HttpRequestMessage management = harness.Request(HttpMethod.Get, "/api/specialize");
        using HttpResponseMessage rejected = await harness.LocalClient.SendAsync(management, timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
    }

    [Theory]
    [InlineData("http://token.test/internal/token", "/MSI/token?future=%2f&future=%2F",
        "http://token.test/internal/token?future=%2f&future=%2F")]
    [InlineData("http://token.test/internal/token?fixed=%2f&resource=platform", "/MSI/token?resource=customer&future=%2F",
        "http://token.test/internal/token?fixed=%2f&resource=platform&resource=customer&future=%2F")]
    [InlineData("http://token.test/internal/token?fixed=1", "/MSI/token",
        "http://token.test/internal/token?fixed=1")]
    [InlineData("http://token.test/internal/token?", "/MSI/token?resource=a",
        "http://token.test/internal/token?&resource=a")]
    [InlineData("http://token.test", "/MSI/token?next=http%3A%2F%2Fother.test%2Fadmin",
        "http://token.test/?next=http%3A%2F%2Fother.test%2Fadmin")]
    public async Task IndependentEndpoints_PreserveRawQueriesAndConfiguredDestination(string endpoint, string target, string expected)
    {
        int requests = 0;
        await using Harness harness = await Harness.CreateAsync((request, _) =>
        {
            Interlocked.Increment(ref requests);
            Assert.Equal(expected, request.RequestUri!.OriginalString);
            Assert.Equal(HostSecret, Assert.Single(request.Headers.GetValues(HttpProxyProtocol.IdentityHeader)));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("token") });
        });
        harness.Configuration["MSI_ENDPOINT"] = endpoint;
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);
        using HttpRequestMessage request = harness.Request(HttpMethod.Get, target);
        request.Headers.Host = "customer-selected.test";
        using HttpResponseMessage response = await harness.LocalClient.SendAsync(request, timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, requests);

        using HttpRequestMessage otherPath = harness.Request(HttpMethod.Get, "/internal/token?resource=a");
        using HttpResponseMessage rejected = await harness.LocalClient.SendAsync(otherPath, timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData("/msi/token")]
    [InlineData("/MSI/token")]
    [InlineData("/MsI/ToKeN")]
    public async Task LocalTokenRoute_IgnoresCaseWithoutChangingUpstreamOrQuery(string path)
    {
        await using Harness harness = await Harness.CreateAsync((request, _) =>
        {
            Assert.Equal("http://token.test/Service/Token?fixed=KeEp%2f&Resource=VaLuE%2f&resource=SECOND%2F",
                request.RequestUri!.OriginalString);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("token") });
        });
        harness.Configuration["MSI_ENDPOINT"] = "http://token.test/Service/Token?fixed=KeEp%2f";
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);
        using HttpRequestMessage request = harness.Request(HttpMethod.Get, $"{path}?Resource=VaLuE%2f&resource=SECOND%2F");

        using HttpResponseMessage response = await harness.LocalClient.SendAsync(request, timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("http://token.test/internal/token?fixed=1#fragment")]
    [InlineData("http://user:secret@token.test/internal/token?fixed=1")]
    [InlineData("http://token.test/api/specialize?fixed=1")]
    public async Task ConfiguredUpstream_StillRejectsFragmentsCredentialsAndManagementPaths(string endpoint)
    {
        await using Harness harness = await Harness.CreateAsync((_, _) =>
            throw new InvalidOperationException("The invalid upstream must not be contacted."));
        harness.Configuration["MSI_ENDPOINT"] = endpoint;
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);

        using HttpResponseMessage response = await harness.SendAsync(timeout.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task HostPreparation_WaitsWithinRequestBudget_BeforeUsingCurrentConfiguration()
    {
        TaskCompletionSource prepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        await using Harness harness = await Harness.CreateAsync((_, _) =>
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("token") });
        });
        harness.DelayHostPreparation(prepared.Task);
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);
        Task<HttpResponseMessage> response = harness.SendAsync(timeout.Token);
        Assert.False(response.IsCompleted);
        Assert.Equal(0, requests);
        prepared.SetResult();

        using HttpResponseMessage completed = await response.WaitAsync(timeout.Token);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task NoRuntimeConnection_RequestDeadlineReturns504WithoutIndefiniteWait()
    {
        await using Harness harness = await Harness.CreateAsync((_, _) =>
            throw new InvalidOperationException("No upstream request is expected."),
            "--WorkerProxy:IdentityProxy:RequestTimeout", "00:00:00.100");
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpResponseMessage response = await harness.SendAsync(timeout.Token);
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    [Fact]
    public async Task RoutineCredentialRotation_DoesNotCancelAlreadySentRequest()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> credentials = [];
        await using Harness harness = await Harness.CreateAsync(async (request, cancellationToken) =>
        {
            lock (credentials)
            {
                credentials.Add(Assert.Single(request.Headers.GetValues(HttpProxyProtocol.IdentityHeader)));
            }

            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("token") };
        });
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);
        Task<HttpResponseMessage> first = harness.SendAsync(timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        harness.Configuration["MSI_SECRET"] = "rotated-host-secret";
        release.SetResult();
        using HttpResponseMessage firstResponse = await first.WaitAsync(timeout.Token);
        using HttpResponseMessage secondResponse = await harness.SendAsync(timeout.Token);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(new[] { HostSecret, "rotated-host-secret" }, credentials);
    }

    [Theory]
    [InlineData(1048576, HttpStatusCode.OK)]
    [InlineData(1048577, HttpStatusCode.RequestEntityTooLarge)]
    public async Task RequestBody_EnforcesExactByteLimit(int length, HttpStatusCode expected)
    {
        int requests = 0;
        await using Harness harness = await Harness.CreateAsync(async (request, cancellationToken) =>
        {
            Interlocked.Increment(ref requests);
            Assert.Equal(length, (await request.Content!.ReadAsByteArrayAsync(cancellationToken)).Length);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("token") };
        });
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);
        using HttpRequestMessage request = harness.Request(HttpMethod.Post);
        request.Content = new ByteArrayContent(new byte[length]);
        using HttpResponseMessage response = await harness.LocalClient.SendAsync(request, timeout.Token);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, requests);
    }

    [Theory]
    [InlineData(1048576, HttpStatusCode.OK)]
    [InlineData(1048577, HttpStatusCode.BadGateway)]
    public async Task ResponseBody_EnforcesExactByteLimit(int length, HttpStatusCode expected)
    {
        await using Harness harness = await Harness.CreateAsync((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[length])
        }));
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);
        using HttpResponseMessage response = await harness.SendAsync(timeout.Token);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal(length, (await response.Content.ReadAsByteArrayAsync(timeout.Token)).Length);
        }
    }

    [Fact]
    public async Task Saturation_Returns429Immediately_ThenRecovers()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using Harness harness = await Harness.CreateAsync(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("token") };
        }, "--WorkerProxy:IdentityProxy:MaxPendingRequests", "1");
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);
        Task<HttpResponseMessage> first = harness.SendAsync(timeout.Token);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            using HttpResponseMessage rejected = await harness.SendAsync(timeout.Token);
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.Equal("1", Assert.Single(rejected.Headers.GetValues("Retry-After")));
        }
        finally
        {
            release.TrySetResult();
        }

        using HttpResponseMessage completed = await first.WaitAsync(timeout.Token);
        using HttpResponseMessage recovered = await harness.SendAsync(timeout.Token);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Fact]
    public async Task Cancellation_ReceivesTerminalAcknowledgment_AndRepeatedRequestsReclaimCapacity()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        await using Harness harness = await Harness.CreateAsync(async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref requests) <= 3)
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    canceled.TrySetResult();
                    throw;
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("token") };
        }, "--WorkerProxy:IdentityProxy:MaxPendingRequests", "1");
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenSource caller = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            Task<HttpResponseMessage> response = harness.SendAsync(caller.Token);
            await entered.Task.WaitAsync(timeout.Token);
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
            await canceled.Task.WaitAsync(timeout.Token);
            await harness.WaitForSingleSlotAsync(timeout.Token);
        }

        using HttpResponseMessage recovered = await harness.SendAsync(timeout.Token);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(4, requests);
    }

    [Fact]
    public async Task Head_PreservesRepresentationLengthWithoutWritingBody()
    {
        await using Harness harness = await Harness.CreateAsync((_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentLength = 321;
            return Task.FromResult(response);
        });
        using CancellationTokenSource timeout = new(TestTimeout);
        await harness.ConnectAsync(timeout.Token);
        using HttpRequestMessage request = harness.Request(HttpMethod.Head);
        using HttpResponseMessage response = await harness.LocalClient.SendAsync(request, timeout.Token);
        Assert.Equal(321, response.Content.Headers.ContentLength);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(timeout.Token));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplication _proxy;
        private readonly Mock<IHttpClientFactory> _clients;
        private readonly HttpClient _upstream;
        private readonly Mock<IOptionsMonitor<ScriptApplicationHostOptions>> _applicationOptions = new();
        private readonly Mock<IScriptWebHostEnvironment> _environment = new();
        private readonly ServiceProvider _services;
        private Task<WorkerLinkResult>? _link;

        private Harness(WebApplication proxy, HttpClient upstream)
        {
            _proxy = proxy;
            _upstream = upstream;
            _clients = new();
            _clients.Setup(factory => factory.CreateClient(HostTokenServiceForwarder.ClientName)).Returns(upstream);
            _applicationOptions.SetupGet(options => options.CurrentValue)
                .Returns(new ScriptApplicationHostOptions { ScriptPath = AppContext.BaseDirectory });
            _environment.SetupGet(environment => environment.DelayCompletionTask).Returns(Task.CompletedTask);
            LocalClient = new() { BaseAddress = proxy.Services.GetRequiredService<ProxyEndpoints>().GetIdentityAddress() };
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MSI_ENDPOINT"] = "http://token.test/MSI/token",
                ["MSI_SECRET"] = HostSecret
            }).Build();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(Configuration);
            services.AddRpcClientServices();
            services.Configure<HttpProxyOptions>(static options => options.Enabled = true);
            services.AddSingleton<IHttpProxyRequestHandler>(new HostTokenServiceForwarder(Configuration, _clients.Object,
                _applicationOptions.Object, _environment.Object, NullLogger<HostTokenServiceForwarder>.Instance));
            Mock<IScriptHostManager> manager = new();
            manager.As<IServiceProvider>().Setup(provider => provider.GetService(typeof(IOptions<ScriptJobHostOptions>)))
                .Returns(Options.Create(new ScriptJobHostOptions { RootScriptPath = AppContext.BaseDirectory }));
            services.AddSingleton(manager.Object);
            services.AddSingleton<IScriptEventManager>(new ScriptEventManager());
            services.AddSingleton(Mock.Of<IEnvironment>());
            services.AddSingleton(_applicationOptions.Object);
            services.AddSingleton(Mock.Of<ISharedMemoryManager>());
            services.AddSingleton(Options.Create(new WorkerConcurrencyOptions()));
            services.AddSingleton(Options.Create(new FunctionsHostingConfigOptions()));
            Mock<IAppCapabilitiesStore> capabilities = new();
            capabilities.Setup(store => store.TrySetAll(It.IsAny<IEnumerable<KeyValuePair<string, string>>>())).Returns(true);
            services.AddSingleton(capabilities.Object);
            services.AddSingleton(Mock.Of<IHttpProxyService>());
            services.AddSingleton(Mock.Of<IMetricsLogger>());
            _services = services.BuildServiceProvider();
            Registry = _services.GetRequiredService<IWorkerChannelRegistry>();
        }

        internal HttpClient LocalClient { get; }

        internal IConfigurationRoot Configuration { get; }

        internal IWorkerChannelRegistry Registry { get; }

        internal void DelayHostPreparation(Task preparation)
            => _environment.SetupGet(environment => environment.DelayCompletionTask).Returns(preparation);

        internal static async Task<Harness> CreateAsync(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, params string[] overrides)
        {
            WebApplication proxy = ProxyApplication.Build(
            [
                "--WEBSITE_POD_NAME", "token-proxy-test",
                "--WorkerProxy:ManagementPort", "0",
                "--WorkerProxy:RuntimeGrpcPort", "0",
                "--WorkerProxy:WorkerGrpcPort", "0",
                "--WorkerProxy:HttpPort", "0",
                "--WorkerProxy:IdentityProxy:Enabled", "true",
                "--WorkerProxy:IdentityProxy:Port", "0",
                "--IDENTITY_HEADER", LocalSecret,
                "--Logging:LogLevel:Default", "Warning",
                .. overrides
            ]);
            await proxy.StartAsync();
            return new Harness(proxy, new HttpClient(new RespondingHandler(respond)));
        }

        internal Task ConnectAsync(CancellationToken cancellationToken)
        {
            UriBuilder endpoint = new(_proxy.Services.GetRequiredService<ProxyEndpoints>().GetRelayAddress(ProxySide.Runtime))
            {
                Host = IPAddress.Loopback.ToString()
            };
            _link = Registry.LinkAsync("customer-run-1", endpoint.Uri, cancellationToken);
            return Task.CompletedTask;
        }

        internal HttpRequestMessage Request(HttpMethod method, string target = "/msi/token?api-version=2019-08-01")
        {
            HttpRequestMessage request = new(method, target);
            request.Headers.Add(HttpProxyProtocol.IdentityHeader, LocalSecret);
            return request;
        }

        internal async Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = Request(HttpMethod.Get);
            return await LocalClient.SendAsync(request, cancellationToken);
        }

        internal async Task WaitForSingleSlotAsync(CancellationToken cancellationToken)
        {
            ProxyIdentityService service = _proxy.Services.GetRequiredService<ProxyIdentityService>();
            while (true)
            {
                using var probe = service.Admission.TryAcquire(1);
                if (probe is not null)
                {
                    return;
                }

                await Task.Delay(10, cancellationToken);
            }
        }

        internal async Task CompleteWorkerHandshakeAsync(CancellationToken cancellationToken)
        {
            Uri address = _proxy.Services.GetRequiredService<ProxyEndpoints>().GetRelayAddress(ProxySide.Worker);
            using GrpcChannel channel = GrpcChannel.ForAddress(address);
            using var worker = new FunctionRpc.FunctionRpcClient(channel).EventStream(cancellationToken: cancellationToken);
            await worker.RequestStream.WriteAsync(new() { StartStream = new() { WorkerId = "customer-run-1" } }, cancellationToken);
            Assert.True(await worker.ResponseStream.MoveNext(cancellationToken));
            Assert.NotNull(worker.ResponseStream.Current.WorkerInitRequest);
            await worker.RequestStream.WriteAsync(new()
            {
                WorkerInitResponse = new() { Result = new() { Status = StatusResult.Types.Status.Success } }
            }, cancellationToken);
            WorkerLinkResult initialized = await _link!.WaitAsync(cancellationToken);
            Assert.Equal("customer-run-1", initialized.Channel.Id);
            Assert.Single(Registry.GetInitializedChannels());
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            if (_link is not null)
            {
                try
                {
                    await _link;
                }
                catch (Exception exception) when (exception is ObjectDisposedException or OperationCanceledException)
                {
                }
            }

            LocalClient.Dispose();
            _upstream.Dispose();
            using CancellationTokenSource timeout = new(TestTimeout);
            await _proxy.StopAsync(timeout.Token);
            await _proxy.DisposeAsync();
        }
    }

    private sealed class RespondingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request, cancellationToken);
    }
}
