// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

extern alias WorkerProxy;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Azure.Functions.Tests;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using ProxyApplication = WorkerProxy::Azure.Functions.WorkerProxy.WorkerProxyApplication;
using ProxyEndpoints = WorkerProxy::Azure.Functions.WorkerProxy.WorkerProxyEndpointConfiguration;
using ProxySide = WorkerProxy::Azure.Functions.WorkerProxy.Rpc.FunctionRpcRelaySide;

namespace Azure.Functions.Host.Tests;

public sealed class WorkerLinkTransportIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Put_NestedIdentity_InitializesWorkerAndPublishesMatchingRuntimeOverRealListeners(bool numericString)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        CancellationToken token = timeout.Token;
        using TestCertificateFiles certificates = new();
        await using WebApplication proxy = ProxyApplication.Build(
        [
            "--WEBSITE_POD_NAME", "worker-proxy-test",
            "--CONTAINER_ENCRYPTION_KEY", ManagementTokenTestData.WorkerKey,
            "--WEBSITE_AUTH_ENCRYPTION_KEY", ManagementTokenTestData.AppKey,
            .. certificates.Arguments,
            "--WorkerProxy:ManagementPort", "0",
            "--WorkerProxy:RuntimeGrpcPort", "0",
            "--WorkerProxy:WorkerGrpcPort", "0",
            "--WorkerProxy:HttpPort", "0",
            "--Logging:LogLevel:Default", "Warning"
        ]);
        await proxy.StartAsync(token);
        ProxyEndpoints endpoints = proxy.Services.GetRequiredService<ProxyEndpoints>();
        using HttpClient management = new() { BaseAddress = Loopback(endpoints.GetManagementAddress()) };
        management.DefaultRequestHeaders.Add(ManagementTokenTestData.HeaderName,
            ManagementTokenTestData.Create(ManagementTokenTestData.WorkerKey, "worker-proxy-test"));
        using HttpResponseMessage assignment = await HttpClientJsonExtensions.PutAsJsonAsync(management, "/admin/worker/assignment", new
        {
            startupMode = "Preconfigured",
            functionAppName = "test-app",
            functionGroupName = "test-group",
            isAlwaysReady = false,
            environment = new Dictionary<string, string>(),
            functionAppDirectory = string.Empty,
            configLastModifiedTime = 1761868800,
            contentLastModifiedTime = 1761868801,
            httpProxyEndpoint = Loopback(endpoints.GetHttpAddress()).GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped)
        }, token);
        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);

        await using ServiceProvider services = CreateRuntimeServices();
        IWorkerChannelRegistry registry = services.GetRequiredService<IWorkerChannelRegistry>();
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(token, registry, useKestrel: true);
        using GrpcChannel workerChannel = GrpcChannel.ForAddress(Loopback(endpoints.GetRelayAddress(ProxySide.Worker)));
        using var worker = new FunctionRpc.FunctionRpcClient(workerChannel).EventStream(cancellationToken: token);
        await worker.RequestStream.WriteAsync(new() { StartStream = new() { WorkerId = "worker-proxy-test" } }, token);

        // Legion's typed primitives serialize as strings and an integer; keep its outer request fields as well.
        var identity = new
        {
            resourceNamespace = "runtime-ns",
            podName = "runtime-\u00e9",
            app = new
            {
                functionAppName = "test-app",
                functionAppSiteId = numericString ? (object)"42" : 42,
                antaresStampName = "test-stamp"
            }
        };
        Uri grpcEndpoint = Loopback(endpoints.GetRelayAddress(ProxySide.Runtime));
        string legacyJson = JsonSerializer.Serialize(new
        {
            workerGrpcEndpoint = grpcEndpoint.AbsoluteUri,
            runtimeAccessKey = ManagementTokenTestData.WorkerKey,
            runtimeIdentity = JsonSerializer.Serialize(identity)
        });
        using HttpResponseMessage legacy = await host.PutAsync(legacyJson, token, "worker-proxy-test");
        Assert.Equal(HttpStatusCode.BadRequest, legacy.StatusCode);
        Assert.Contains("InvalidBody", await legacy.Content.ReadAsStringAsync(token), StringComparison.Ordinal);
        Assert.Empty(registry.GetInitializedChannels());

        string json = JsonSerializer.Serialize(new
        {
            workerNamespace = "worker-ns",
            workerPodName = "worker-proxy-test",
            workerGrpcEndpoint = grpcEndpoint.AbsoluteUri,
            runtimeIdentity = identity,
            runtimeAccessKey = ManagementTokenTestData.WorkerKey
        });
        Task<HttpResponseMessage> link = host.PutAsync(json, token, "worker-proxy-test");
        Assert.True(await worker.ResponseStream.MoveNext(token));
        Assert.NotNull(worker.ResponseStream.Current.WorkerInitRequest);
        await worker.RequestStream.WriteAsync(new()
        {
            RequestId = worker.ResponseStream.Current.RequestId,
            WorkerInitResponse = new() { Result = new() { Status = StatusResult.Types.Status.Success } }
        }, token);
        using HttpResponseMessage linked = await link;
        Assert.Equal(HttpStatusCode.Created, linked.StatusCode);
        Assert.Empty(await linked.Content.ReadAsStringAsync(token));
        Assert.Single(registry.GetInitializedChannels());

        using HttpResponseMessage checkpoint = await HttpClientJsonExtensions.PostAsJsonAsync<object?>(management, "/admin/infra/instanceState", null, token);
        Assert.Equal(HttpStatusCode.OK, checkpoint.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await checkpoint.Content.ReadAsStringAsync(token));
        JsonElement state = document.RootElement;
        Assert.Equal("Running", state.GetProperty("status").GetString());
        JsonElement workerLink = state.GetProperty("link");
        Assert.Equal("Linked", workerLink.GetProperty("state").GetString());
        JsonElement runtime = workerLink.GetProperty("runtime");
        Assert.Equal("runtime-ns", runtime.GetProperty("resourceNamespace").GetString());
        Assert.Equal("runtime-\u00e9", runtime.GetProperty("podName").GetString());
        JsonElement app = runtime.GetProperty("app");
        Assert.Equal("test-app", app.GetProperty("functionAppName").GetString());
        Assert.Equal(42, app.GetProperty("functionAppSiteId").GetInt32());
        Assert.Equal("test-stamp", app.GetProperty("antaresStampName").GetString());

        using HttpResponseMessage retry = await host.PutAsync(json, token, "worker-proxy-test");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Single(registry.GetInitializedChannels());
    }

    private static Uri Loopback(Uri address) => new UriBuilder(address) { Host = IPAddress.Loopback.ToString() }.Uri;

    private static ServiceProvider CreateRuntimeServices()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WEBSITE_SITE_NAME"] = "test-app",
            ["WEBSITE_AUTH_ENCRYPTION_KEY"] = ManagementTokenTestData.AppKey
        }).Build());
        services.AddRpcClientServices();
        Mock<IScriptHostManager> manager = new();
        manager.As<IServiceProvider>().Setup(provider => provider.GetService(typeof(IOptions<ScriptJobHostOptions>)))
            .Returns(Options.Create(new ScriptJobHostOptions { RootScriptPath = AppContext.BaseDirectory }));
        services.AddSingleton(manager.Object);
        services.AddSingleton<IScriptEventManager>(new ScriptEventManager());
        services.AddSingleton(Mock.Of<IEnvironment>());
        services.AddSingleton(Mock.Of<ISharedMemoryManager>());
        services.AddSingleton(Options.Create(new WorkerConcurrencyOptions()));
        services.AddSingleton(Options.Create(new FunctionsHostingConfigOptions()));
        services.AddSingleton(Mock.Of<IHttpProxyService>());
        services.AddSingleton(Mock.Of<IMetricsLogger>());
        Mock<IOptionsMonitor<ScriptApplicationHostOptions>> applicationOptions = new();
        applicationOptions.SetupGet(options => options.CurrentValue)
            .Returns(new ScriptApplicationHostOptions { ScriptPath = AppContext.BaseDirectory });
        services.AddSingleton(applicationOptions.Object);
        Mock<IScriptWebHostEnvironment> environment = new();
        environment.SetupGet(value => value.DelayCompletionTask).Returns(Task.CompletedTask);
        services.AddSingleton(environment.Object);
        Mock<IAppCapabilitiesStore> capabilities = new();
        capabilities.Setup(store => store.TrySetAll(It.IsAny<IEnumerable<KeyValuePair<string, string>>>())).Returns(true);
        services.AddSingleton(capabilities.Object);
        return services.BuildServiceProvider();
    }
}
