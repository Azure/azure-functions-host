// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

extern alias workerproxy;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Azure.Functions.Rpc.Client;
using Azure.Functions.Tests;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;
using ProxyApplication = workerproxy::Azure.Functions.WorkerProxy.WorkerProxyApplication;
using ProxyAuthentication = workerproxy::Azure.Functions.Authentication.RuntimeWorkerAuthentication;
using ProxyCertificate = workerproxy::Azure.Functions.WorkerProxy.Authentication.WorkerProxyCertificate;
using ProxyEndpoints = workerproxy::Azure.Functions.WorkerProxy.WorkerProxyEndpointConfiguration;
using ProxyOptions = workerproxy::Azure.Functions.WorkerProxy.WorkerProxyOptions;
using ProxyRelay = workerproxy::Azure.Functions.WorkerProxy.Rpc.FunctionRpcRelay;
using ProxyRelaySide = workerproxy::Azure.Functions.WorkerProxy.Rpc.FunctionRpcRelaySide;
using ProxyStateManager = workerproxy::Azure.Functions.WorkerProxy.State.WorkerPodStateManager;

namespace Azure.Functions.Host.Tests;

public class WorkerRuntimeAuthenticationIntegrationTests(ITestOutputHelper output)
{
    private const string WorkerTokenHeader = "x-ms-functions-worker-token";
    private const string AppTokenHeader = "x-ms-functions-app-token";
    private const string AppKeySetting = "WEBSITE_AUTH_ENCRYPTION_KEY";

    [Theory]
    [InlineData(null)]
    [InlineData((StatusResult.Types.Status)42)]
    [InlineData(StatusResult.Types.Status.Success)]
    [InlineData(StatusResult.Types.Status.Failure)]
    [InlineData(StatusResult.Types.Status.Cancelled)]
    public async Task Link_AuthenticatedInitializationResultIsValidatedAtReceivingBoundary(StatusResult.Types.Status? status)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using TestCertificateFiles certificates = new();
        using ProxyCertificate certificate = new(Options.Create(new ProxyOptions
        {
            CertificatePath = certificates.CertificatePath,
            CertificateKeyPath = certificates.KeyPath
        }));
        byte[] appKey = SHA256.HashData("integration-test-app-key"u8);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = Convert.ToBase64String("""{"alg":"HS256","typ":"JWT"}"""u8).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string payload = $$"""{"aud":"azure-functions-app:test-app","purpose":"app","worker_id":"test-worker-pod","iat":{{now}},"nbf":{{now}},"exp":{{now + 300}}}""";
        string content = $"{header}.{Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
        string proof = $"{content}.{Convert.ToBase64String(HMACSHA256.HashData(appKey, Encoding.ASCII.GetBytes(content))).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
        AuthenticatedInitializationService service = new(status, proof);
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener =>
        {
            listener.Protocols = HttpProtocols.Http2;
            listener.UseHttps(certificate.Certificate);
        }));
        builder.Services.AddSingleton(service);
        builder.Services.AddGrpc();
        await using WebApplication endpoint = builder.Build();
        endpoint.MapGrpcService<AuthenticatedInitializationService>();
        await endpoint.StartAsync(timeout.Token);
        Uri grpc = new(endpoint.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        ConcurrentQueue<(string Message, Exception? Error)> diagnostics = new();
        await using ServiceProvider runtime = CreateRuntime("test-app", Convert.ToHexString(appKey), true, diagnostics, new());
        IWorkerChannelRegistry registry = runtime.GetRequiredService<IWorkerChannelRegistry>();
        await using WorkerLinkTestHost receiver = await WorkerLinkTestHost.StartAsync(timeout.Token, registry, useKestrel: true);
        string json = $$"""
            {"workerPodName":"test-worker-pod","workerGrpcEndpoint":"{{grpc.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped)}}","runtimeAccessKey":"{{ManagementTokenTestData.WorkerKey}}"}
            """;

        using HttpResponseMessage response = await receiver.PutAsync(json, timeout.Token, "test-worker-pod");

        Assert.True(service.CallerAuthenticated);
        bool success = status is StatusResult.Types.Status.Success;
        Assert.Equal(success ? HttpStatusCode.Created : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        if (success)
        {
            Assert.Single(registry.GetInitializedChannels());
        }
        else
        {
            Assert.Contains("WorkerUnavailable", await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
            Assert.Empty(registry.GetInitializedChannels());
            Assert.False(registry.TryGetInitializedChannel("test-worker-pod", out _));
            await service.Disconnected.WaitAsync(timeout.Token);
            if (status is null or (StatusResult.Types.Status)42)
            {
                Assert.DoesNotContain(diagnostics, entry => entry.Message.Contains("Worker capabilities:", StringComparison.Ordinal)
                    || entry.Message.Contains("Worker process initialized", StringComparison.Ordinal)
                    || entry.Message.Contains("must-not-be-published", StringComparison.Ordinal));
            }
            else
            {
                Assert.Contains(diagnostics, entry =>
                    string.Equals(entry.Message, "Initializing worker process failed", StringComparison.Ordinal)
                    && (status is StatusResult.Types.Status.Cancelled
                        ? entry.Error is TaskCanceledException
                        : entry.Error is Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException
                            && entry.Error.Message.Contains("original-worker-initialization-error", StringComparison.Ordinal)));
            }
        }
        using CancellationTokenSource readyWait = new();
        Task<IReadOnlyList<WorkerChannel>> ready = registry.GetReadyChannelsAsync().AsTask().WaitAsync(readyWait.Token);
        Assert.False(ready.IsCompleted);
        readyWait.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ready);
        Assert.DoesNotContain(diagnostics, entry => entry.Message.Contains(proof, StringComparison.Ordinal)
            || entry.Message.Contains("WorkerProxyAppToken", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-an-absolute-uri")]
    [InlineData("http://worker-proxy:28080/")]
    [InlineData("https://worker-proxy:28080/customer")]
    [InlineData("https://user@worker-proxy:28080/")]
    public async Task Link_InvalidAuthenticatedAdvertisedHttpOriginReturnsRetryable503OnRealKestrel(string origin)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using TestCertificateFiles certificates = new();
        using ProxyCertificate certificate = new(Options.Create(new ProxyOptions
        {
            CertificatePath = certificates.CertificatePath,
            CertificateKeyPath = certificates.KeyPath
        }));
        byte[] appKey = SHA256.HashData("integration-test-app-key"u8);
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener =>
        {
            listener.Protocols = HttpProtocols.Http2;
            listener.UseHttps(certificate.Certificate);
        }));
        builder.Services.AddSingleton(new AdvertisedOriginService(origin, appKey));
        builder.Services.AddGrpc();
        await using WebApplication endpoint = builder.Build();
        endpoint.MapGrpcService<AdvertisedOriginService>();
        await endpoint.StartAsync(timeout.Token);
        Uri grpc = new(endpoint.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        await using ServiceProvider runtime = CreateRuntime("test-app", Convert.ToHexString(appKey), true, new(), new());
        IWorkerChannelRegistry registry = runtime.GetRequiredService<IWorkerChannelRegistry>();
        await using WorkerLinkTestHost receiver = await WorkerLinkTestHost.StartAsync(timeout.Token, registry, useKestrel: true);
        string json = $$"""
            {"workerPodName":"test-worker-pod","workerGrpcEndpoint":"{{grpc.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped)}}","runtimeAccessKey":"{{ManagementTokenTestData.WorkerKey}}"}
            """;

        using HttpResponseMessage response = await receiver.PutAsync(json, timeout.Token, "test-worker-pod");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("WorkerUnavailable", await response.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        Assert.Empty(registry.GetInitializedChannels());
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("pre-init-retry")]
    [InlineData("missing-proxy-key")]
    [InlineData("wrong-proxy-key")]
    [InlineData("missing-runtime-key")]
    [InlineData("failed-init")]
    [InlineData("failed-init-missing-proxy-key")]
    [InlineData("failed-init-wrong-proxy-key")]
    [InlineData("failed-init-missing-runtime-key")]
    [InlineData("cancelled-init")]
    [InlineData("unknown-init-status")]
    [InlineData("null-init-result")]
    public async Task LinkAndHttpInvocation_UseCanonicalMvcRouteAndBidirectionalProofsOverTls(string scenario)
    {
        const string workerId = "test-worker-pod";
        const string appName = "TeSt-App";
        const string initializationError = "customer-worker-initialization-failure";
        bool initializationFails = scenario.StartsWith("failed-init", StringComparison.Ordinal)
            || string.Equals(scenario, "cancelled-init", StringComparison.Ordinal);
        bool malformedResult = scenario is "unknown-init-status" or "null-init-result";
        bool preInitRetry = string.Equals(scenario, "pre-init-retry", StringComparison.Ordinal);
        ConcurrentQueue<(string Message, Exception? Error)> diagnostics = new();
        ConcurrentQueue<Uri> forwardedOrigins = new();
        ConcurrentQueue<string> customerPaths = new();
        string appKey = Convert.ToHexString(SHA256.HashData("integration-test-app-key"u8));
        bool native = string.Equals(scenario, "valid", StringComparison.Ordinal)
            && Environment.GetEnvironmentVariable("FUNCTIONS_WORKERPROXY_NATIVE_MANAGEMENT_URI") is not null;
        using TestCertificateFiles certificates = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        WebApplicationBuilder workerBuilder = WebApplication.CreateSlimBuilder();
        workerBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using WebApplication customerHttp = workerBuilder.Build();
        customerHttp.Run(async context =>
        {
            Assert.Equal("Bearer customer-token", context.Request.Headers.Authorization);
            Assert.False(context.Request.Headers.ContainsKey(WorkerTokenHeader));
            Assert.False(context.Request.Headers.ContainsKey(AppTokenHeader));
            Assert.False(context.Request.Headers.ContainsKey(ManagementTokenTestData.HeaderName));
            customerPaths.Enqueue(context.Request.Path.ToString());
            string body = await new StreamReader(context.Request.Body).ReadToEndAsync(context.RequestAborted);
            await context.Response.WriteAsync($"received:{body}", context.RequestAborted);
        });
        await customerHttp.StartAsync(timeout.Token);
        string customerAddress = customerHttp.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        await using WebApplication? proxy = native ? null : ProxyApplication.Build(
        [
            "--WEBSITE_POD_NAME", workerId,
            "--CONTAINER_ENCRYPTION_KEY", ManagementTokenTestData.WorkerKey,
            "--WEBSITE_AUTH_ENCRYPTION_KEY", scenario switch
            {
                "missing-proxy-key" or "failed-init-missing-proxy-key" => string.Empty,
                "wrong-proxy-key" or "failed-init-wrong-proxy-key" => Convert.ToHexString(SHA256.HashData("different-app-key"u8)),
                _ => appKey
            },
            "--WorkerProxy:CertificatePath", certificates.CertificatePath,
            "--WorkerProxy:CertificateKeyPath", certificates.KeyPath,
            "--WorkerProxy:ManagementPort", "0",
            "--WorkerProxy:RuntimeGrpcPort", "0",
            "--WorkerProxy:WorkerGrpcPort", "0",
            "--WorkerProxy:HttpPort", "0",
            "--Logging:LogLevel:Default", "Warning"
        ]);
        if (proxy is not null)
        {
            await proxy.StartAsync(timeout.Token);
        }

        ProxyEndpoints? endpoints = proxy?.Services.GetRequiredService<ProxyEndpoints>();
        await using TcpPortMapping? grpcMapping = endpoints is null ? null : new(Loopback(endpoints.GetRelayAddress(ProxyRelaySide.Runtime)));
        await using TcpPortMapping? httpMapping = endpoints is null ? null : new(Loopback(endpoints.GetHttpAddress()));
        Uri grpcEndpoint = grpcMapping?.Endpoint ?? NativeAddress("GRPC");
        Uri httpEndpoint = httpMapping?.Endpoint ?? NativeAddress("HTTP");
        int grpcListenerPort = endpoints?.GetRelayAddress(ProxyRelaySide.Runtime).Port ?? NativeAddress("GRPC_LISTENER").Port;
        int httpListenerPort = endpoints?.GetHttpAddress().Port ?? NativeAddress("HTTP_LISTENER").Port;
        Assert.Equal(Uri.UriSchemeHttps, grpcEndpoint.Scheme);
        Assert.Equal(Uri.UriSchemeHttps, httpEndpoint.Scheme);
        Assert.NotEqual(grpcListenerPort, grpcEndpoint.Port);
        Assert.NotEqual(httpListenerPort, httpEndpoint.Port);
        output.WriteLine($"Actual mapped HTTPS gRPC port {grpcEndpoint.Port} -> listener {grpcListenerPort}; HTTP port {httpEndpoint.Port} -> listener {httpListenerPort}.");
        if (proxy is not null)
        {
            Assert.Null(proxy.Configuration["WorkerProxy:HttpProxyEndpoint"]);
        }

        using HttpClient management = new()
        {
            BaseAddress = endpoints is null ? NativeAddress("MANAGEMENT") : Loopback(endpoints.GetManagementAddress())
        };
        management.DefaultRequestHeaders.Add(ManagementTokenTestData.HeaderName,
            ManagementTokenTestData.Create(ManagementTokenTestData.WorkerKey, workerId));
        using (HttpRequestMessage initialPoll = new(HttpMethod.Post, "/admin/infra/instanceState"))
        {
            initialPoll.Headers.Add("x-ms-functions-pod-name", workerId);
            using HttpResponseMessage initialState = await management.SendAsync(initialPoll, timeout.Token);
            Assert.Equal(HttpStatusCode.NoContent, initialState.StatusCode);
        }
        string assignmentJson = $$"""
            {"startupMode":"Preconfigured","functionAppName":"test-app","functionGroupName":"group","isAlwaysReady":false,
             "environment":{"PRIVATE_SETTING":"not-customer-config"},"functionAppDirectory":"",
             "configLastModifiedTime":9007199254740993,"contentLastModifiedTime":1761868801,"maxHttpConcurrency":0,
             "httpProxyEndpoint":"{{httpEndpoint.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped)}}"}
            """;
        using StringContent assignmentContent = new(assignmentJson, Encoding.UTF8, "application/json");
        using HttpResponseMessage assignment = await management.PutAsync("/admin/worker/assignment", assignmentContent, timeout.Token);
        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        if (proxy is not null)
        {
            Assert.False(proxy.Services.GetRequiredService<ProxyStateManager>().State.IsWorkerAttached);
            Assert.Null(proxy.Services.GetRequiredService<IConfiguration>()["PRIVATE_SETTING"]);
        }
        foreach (string field in new[] { "configLastModifiedTime", "contentLastModifiedTime", "maxHttpConcurrency" })
        {
            JsonObject changed = JsonNode.Parse(assignmentJson)!.AsObject();
            changed[field] = changed[field]!.GetValue<long>() + 1;
            using StringContent changedContent = new(changed.ToJsonString(), Encoding.UTF8, "application/json");
            using HttpResponseMessage conflict = await management.PutAsync("/admin/worker/assignment", changedContent, timeout.Token);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Contains("AssignmentConflict", await conflict.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
        }
        using StringContent exactContent = new(assignmentJson, Encoding.UTF8, "application/json");
        using HttpResponseMessage exactRetry = await management.PutAsync("/admin/worker/assignment", exactContent, timeout.Token);
        Assert.Equal(HttpStatusCode.NoContent, exactRetry.StatusCode);
        JsonObject numericStringRetry = JsonNode.Parse(assignmentJson)!.AsObject();
        numericStringRetry["configLastModifiedTime"] = "9007199254740993";
        numericStringRetry["contentLastModifiedTime"] = "1761868801";
        numericStringRetry["maxHttpConcurrency"] = "0";
        using StringContent equivalentContent = new(numericStringRetry.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage equivalentRetry = await management.PutAsync("/admin/worker/assignment", equivalentContent, timeout.Token);
        Assert.Equal(HttpStatusCode.NoContent, equivalentRetry.StatusCode);

        await using ServiceProvider runtime = CreateRuntime(appName, appKey,
            includeAppKey: scenario is not ("missing-runtime-key" or "failed-init-missing-runtime-key"),
            diagnostics, forwardedOrigins);
        IWorkerChannelRegistry registry = runtime.GetRequiredService<IWorkerChannelRegistry>();
        await using WorkerLinkTestHost managementReceiver = await WorkerLinkTestHost.StartAsync(
            timeout.Token, registry, useKestrel: true);
        string json = $$"""
            {"workerPodName":"test-worker-pod","workerGrpcEndpoint":"{{grpcEndpoint.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped)}}","runtimeAccessKey":"{{ManagementTokenTestData.WorkerKey}}"}
            """;
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        Task<HttpResponseMessage>? linking = preInitRetry ? null : managementReceiver.Client.PutAsync(
            $"/admin/workers/{Uri.EscapeDataString(workerId)}", content, timeout.Token);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.False(linking?.IsCompleted ?? false);

        using GrpcChannel localWorkerChannel = GrpcChannel.ForAddress(
            endpoints is null ? NativeAddress("WORKER_GRPC") : Loopback(endpoints.GetRelayAddress(ProxyRelaySide.Worker)));
        FunctionRpc.FunctionRpcClient localWorker = new(localWorkerChannel);
        using AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> workerCall =
            localWorker.EventStream(cancellationToken: timeout.Token);
        await workerCall.RequestStream.WriteAsync(new StreamingMessage { StartStream = new() { WorkerId = workerId } }, timeout.Token);
        if (preInitRetry)
        {
            using GrpcChannel oldChannel = GrpcChannel.ForAddress(grpcEndpoint, new GrpcChannelOptions
            {
                HttpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                }
            });
            Metadata headers = new();
            foreach ((string name, string token) in ProxyAuthentication.CreateHeaders(
                Convert.FromBase64String(ManagementTokenTestData.WorkerKey), workerId, DateTimeOffset.UtcNow))
            {
                headers.Add(name, token);
            }
            using AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> old = new FunctionRpc.FunctionRpcClient(oldChannel)
                .EventStream(headers, cancellationToken: timeout.Token);
            Assert.NotNull((await ReadAsync(old, timeout.Token)).StartStream);
            var accepted = proxy!.Services.GetRequiredService<ProxyStateManager>().State;
            await old.RequestStream.CompleteAsync();
            Grpc.Core.RpcException dropped = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => old.ResponseStream.MoveNext(timeout.Token));
            Assert.Equal(Grpc.Core.StatusCode.Unavailable, dropped.StatusCode);
            while (proxy.Services.GetRequiredService<ProxyRelay>().IsAttached(ProxyRelaySide.Runtime))
            {
                await Task.Delay(10, timeout.Token);
            }
            Assert.Same(accepted, proxy.Services.GetRequiredService<ProxyStateManager>().State);
            linking = managementReceiver.Client.PutAsync($"/admin/workers/{Uri.EscapeDataString(workerId)}", content, timeout.Token);
        }
        StreamingMessage init = await ReadAsync(workerCall, timeout.Token);
        Assert.NotNull(init.WorkerInitRequest);
        StatusResult? initializationResult = scenario switch
        {
            "unknown-init-status" => new() { Status = (StatusResult.Types.Status)42 },
            "null-init-result" => null,
            _ when initializationFails => new()
            {
                Status = string.Equals(scenario, "cancelled-init", StringComparison.Ordinal)
                    ? StatusResult.Types.Status.Cancelled
                    : StatusResult.Types.Status.Failure,
                Exception = new() { Message = initializationError }
            },
            _ => Success()
        };
        await workerCall.RequestStream.WriteAsync(new StreamingMessage
        {
            RequestId = init.RequestId,
            WorkerInitResponse = new()
            {
                Result = initializationResult,
                Capabilities =
                {
                    ["HttpUri"] = malformedResult ? httpEndpoint.AbsoluteUri : new Uri(new Uri(customerAddress), "/before/").AbsoluteUri,
                    ["WorkerProxyAppToken"] = "customer-init-proof-must-not-escape"
                }
            }
        }, timeout.Token);
        using HttpResponseMessage linkResponse = await linking!;
        if (scenario is not ("valid" or "pre-init-retry"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, linkResponse.StatusCode);
            Assert.Empty(registry.GetInitializedChannels());
            Assert.False(registry.TryGetInitializedChannel(workerId, out _));
            if (initializationFails || malformedResult)
            {
                bool workerErrorLogged = diagnostics.Any(entry =>
                    string.Equals(entry.Message, "Initializing worker process failed", StringComparison.Ordinal)
                    && (string.Equals(scenario, "cancelled-init", StringComparison.Ordinal)
                        ? entry.Error is TaskCanceledException
                        : entry.Error is Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException
                            && entry.Error.Message.Contains(initializationError, StringComparison.Ordinal)));
                Assert.Equal(scenario is "failed-init" or "cancelled-init", workerErrorLogged);
                Assert.DoesNotContain(initializationError, await linkResponse.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
            }

            return;
        }

        Assert.Equal(HttpStatusCode.Created, linkResponse.StatusCode);
        Assert.Empty(await linkResponse.Content.ReadAsStringAsync(timeout.Token));
        Assert.True(registry.TryGetInitializedChannel(workerId, out WorkerChannel channel));

        FunctionMetadata function = new() { Name = "Http", Language = "external" };
        function.Bindings.Add(new() { Name = "req", Type = "httpTrigger", Direction = BindingDirection.In });
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), null, timeout.Token);
        StreamingMessage load = await ReadAsync(workerCall, timeout.Token);
        Assert.NotNull(load.FunctionLoadRequest);
        await workerCall.RequestStream.WriteAsync(new StreamingMessage
        {
            RequestId = load.RequestId,
            FunctionLoadResponse = new() { FunctionId = function.GetFunctionId(), Result = Success() }
        }, timeout.Token);
        Assert.Same(channel, Assert.Single(await registry.GetReadyChannelsAsync().AsTask().WaitAsync(timeout.Token)));
        await InvokeAsync(channel, function, workerCall, "before-reload", timeout.Token);
        Assert.Equal(httpEndpoint, Assert.Single(forwardedOrigins));
        Assert.Equal(["/before/invoke"], customerPaths.ToArray());

        Task<bool> reload = channel.SendFunctionEnvironmentReloadRequest();
        StreamingMessage reloadRequest = await ReadAsync(workerCall, timeout.Token);
        Assert.NotNull(reloadRequest.FunctionEnvironmentReloadRequest);
        await workerCall.RequestStream.WriteAsync(new StreamingMessage
        {
            RequestId = reloadRequest.RequestId,
            FunctionEnvironmentReloadResponse = new()
            {
                Result = Success(),
                CapabilitiesUpdateStrategy = FunctionEnvironmentReloadResponse.Types.CapabilitiesUpdateStrategy.Replace,
                Capabilities =
                {
                    ["HttpUri"] = new Uri(new Uri(customerAddress), "/after/").AbsoluteUri,
                    ["WorkerProxyAppToken"] = "customer-reload-proof-must-not-escape"
                }
            }
        }, timeout.Token);
        Assert.True(await reload.WaitAsync(timeout.Token));
        await InvokeAsync(channel, function, workerCall, "after-reload", timeout.Token);
        Assert.Equal([httpEndpoint, httpEndpoint], forwardedOrigins.ToArray());
        Assert.Equal(["/before/invoke", "/after/invoke"], customerPaths.ToArray());
        Assert.DoesNotContain(diagnostics, entry => entry.Message.Contains("customer-init-proof-must-not-escape", StringComparison.Ordinal)
            || entry.Message.Contains("customer-reload-proof-must-not-escape", StringComparison.Ordinal));
        output.WriteLine($"Init and real reload retained assigned origin {httpEndpoint}; both HTTP invocations succeeded through the mapping.");

        using StringContent stateBody = new("null", Encoding.UTF8, "application/json");
        using HttpResponseMessage stateResponse = await management.PostAsync("/admin/infra/instanceState", stateBody, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        string state = await stateResponse.Content.ReadAsStringAsync(timeout.Token);
        Assert.StartsWith("""{"functionsContainerType":"FunctionsSplitWorkerProxy",""", state, StringComparison.Ordinal);
        Assert.Contains(""""status":"Starting"""", state, StringComparison.Ordinal);
        Assert.Contains(""""state":"Unlinked"""", state, StringComparison.Ordinal);
        Assert.DoesNotContain("configLastModifiedTime", state, StringComparison.Ordinal);
        Assert.DoesNotContain("contentLastModifiedTime", state, StringComparison.Ordinal);
        Assert.DoesNotContain("maxHttpConcurrency", state, StringComparison.Ordinal);
        Assert.DoesNotContain(appKey, state, StringComparison.Ordinal);
        Assert.DoesNotContain(ManagementTokenTestData.WorkerKey, state, StringComparison.Ordinal);
        JsonObject replay = JsonNode.Parse(state)!.AsObject();
        replay["revisionId"] = 0;
        replay["configLastModifiedTime"] = 0;
        replay["contentLastModifiedTime"] = 0;
        replay["link"]!.AsObject().Remove("runtime");
        using StringContent previousStateBody = new(replay.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage replayResponse = await management.PostAsync("/admin/infra/instanceState", previousStateBody, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(state, await replayResponse.Content.ReadAsStringAsync(timeout.Token));
    }

    private static async Task InvokeAsync(WorkerChannel channel, FunctionMetadata function,
        AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> workerCall, string payload, CancellationToken cancellationToken)
    {
        DefaultHttpContext httpContext = new();
        using MemoryStream requestBody = new(Encoding.UTF8.GetBytes(payload));
        using MemoryStream responseBody = new();
        httpContext.Request.Method = "POST";
        httpContext.Request.Scheme = Uri.UriSchemeHttps;
        httpContext.Request.Host = new("customer.example");
        httpContext.Request.Path = "/invoke";
        httpContext.Request.Body = requestBody;
        httpContext.Request.ContentLength = requestBody.Length;
        httpContext.Request.Headers.Authorization = "Bearer customer-token";
        httpContext.Request.Headers[WorkerTokenHeader] = "caller-supplied-worker-token";
        httpContext.Request.Headers[AppTokenHeader] = "caller-supplied-app-token";
        httpContext.Request.Headers[ManagementTokenTestData.HeaderName] = "caller-supplied-site-token";
        httpContext.Response.Body = responseBody;
        httpContext.Items[ScriptConstants.AzureFunctionsHttpTriggerContext] = new object();
        ScriptInvocationContext invocation = new()
        {
            FunctionMetadata = function,
            ExecutionContext = new() { FunctionName = function.Name, InvocationId = Guid.NewGuid() },
            BindingData = [],
            Inputs = [("req", DataType.String, httpContext.Request)],
            ResultSource = new(TaskCreationOptions.RunContinuationsAsynchronously),
            AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),
            Logger = NullLogger.Instance
        };
        Assert.True(await channel.FunctionInputBuffers[function.GetFunctionId()].SendAsync(invocation, cancellationToken));
        StreamingMessage invoked = await ReadAsync(workerCall, cancellationToken);
        Assert.Equal(new RpcHttp(), Assert.Single(invoked.InvocationRequest.InputData).Data.Http);
        await workerCall.RequestStream.WriteAsync(new StreamingMessage
        {
            RequestId = invoked.RequestId,
            InvocationResponse = new() { InvocationId = invoked.InvocationRequest.InvocationId, Result = Success() }
        }, cancellationToken);

        await invocation.ResultSource.Task.WaitAsync(cancellationToken);
        Assert.Equal($"received:{payload}", Encoding.UTF8.GetString(responseBody.ToArray()));
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
    }

    private static ServiceProvider CreateRuntime(
        string appName, string appKey, bool includeAppKey, ConcurrentQueue<(string Message, Exception? Error)> diagnostics,
        ConcurrentQueue<Uri> forwardedOrigins)
    {
        Mock<IScriptHostManager> hostManager = new();
        hostManager.As<IServiceProvider>().Setup(provider => provider.GetService(typeof(IOptions<ScriptJobHostOptions>)))
            .Returns(Options.Create(new ScriptJobHostOptions { RootScriptPath = Path.GetTempPath() }));
        Mock<IOptionsMonitor<ScriptApplicationHostOptions>> applicationOptions = new();
        applicationOptions.SetupGet(options => options.CurrentValue)
            .Returns(new ScriptApplicationHostOptions { ScriptPath = Path.GetTempPath() });
        Mock<IAppCapabilitiesStore> capabilities = new();
        capabilities.Setup(store => store.TrySetAll(It.IsAny<IEnumerable<KeyValuePair<string, string>>>())).Returns(true);
        ServiceCollection services = new();
        Mock<ILogger> logger = new();
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        logger.Setup(value => value.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(call => diagnostics.Enqueue((
                (string)((Delegate)call.Arguments[4]).DynamicInvoke(call.Arguments[2], call.Arguments[3])!,
                (Exception?)call.Arguments[3]))));
        Mock<ILoggerProvider> provider = new();
        provider.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(provider.Object));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WEBSITE_SITE_NAME"] = appName,
            [AppKeySetting] = includeAppKey ? appKey : null
        }).Build());
        services.AddSingleton<IScriptEventManager>(new ScriptEventManager());
        services.AddSingleton(hostManager.Object);
        services.AddSingleton(Mock.Of<IEnvironment>());
        services.AddSingleton(applicationOptions.Object);
        services.AddSingleton(Mock.Of<ISharedMemoryManager>());
        services.AddSingleton<IOptions<WorkerConcurrencyOptions>>(Options.Create(new WorkerConcurrencyOptions()));
        services.AddSingleton<IOptions<FunctionsHostingConfigOptions>>(Options.Create(new FunctionsHostingConfigOptions()));
        services.AddSingleton(capabilities.Object);
        services.AddSingleton(Mock.Of<IMetricsLogger>());
        services.AddSingleton(Mock.Of<IHttpProxyService>(MockBehavior.Strict));
        services.AddRpcClientServices();
        ServiceDescriptor forwarding = services.Single(service => service.ServiceType == typeof(Func<IHttpProxyService>));
        services.Remove(forwarding);
        services.Add(ServiceDescriptor.Singleton<Func<IHttpProxyService>>(provider =>
        {
            Func<IHttpProxyService> inner = (Func<IHttpProxyService>)forwarding.ImplementationFactory!(provider);

            return () => new RecordingHttpProxyService(inner(), forwardedOrigins);
        }));

        return services.BuildServiceProvider();
    }

    private static StatusResult Success() => new() { Status = StatusResult.Types.Status.Success };

    private static async Task<StreamingMessage> ReadAsync(
        AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> call, CancellationToken cancellationToken)
    {
        Assert.True(await call.ResponseStream.MoveNext(cancellationToken));

        return call.ResponseStream.Current;
    }

    private static Uri Loopback(Uri uri) => new UriBuilder(uri) { Host = IPAddress.Loopback.ToString() }.Uri;

    private static Uri NativeAddress(string role)
        => new(Environment.GetEnvironmentVariable($"FUNCTIONS_WORKERPROXY_NATIVE_{role}_URI")
            ?? throw new InvalidOperationException($"The native {role} test endpoint is required."));

    private sealed class RecordingHttpProxyService(IHttpProxyService inner, ConcurrentQueue<Uri> origins) : IHttpProxyService
    {
        public void StartForwarding(ScriptInvocationContext context, Uri httpUri)
        {
            origins.Enqueue(httpUri);
            inner.StartForwarding(context, httpUri);
        }

        public Task EnsureSuccessfulForwardingAsync(ScriptInvocationContext context) => inner.EnsureSuccessfulForwardingAsync(context);
    }

    private sealed class AuthenticatedInitializationService(StatusResult.Types.Status? status, string proof) : FunctionRpc.FunctionRpcBase
    {
        private readonly TaskCompletionSource _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool CallerAuthenticated { get; private set; }

        internal Task Disconnected => _disconnected.Task;

        public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream,
            IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
        {
            CallerAuthenticated = ProxyAuthentication.ValidateHeaders(
                name => context.RequestHeaders.GetValue(name), Convert.FromBase64String(ManagementTokenTestData.WorkerKey),
                "test-worker-pod", DateTimeOffset.UtcNow);
            if (!CallerAuthenticated)
            {
                throw new Grpc.Core.RpcException(new Status(StatusCode.Unauthenticated, "A worker caller token is required."));
            }

            try
            {
                await responseStream.WriteAsync(new() { StartStream = new() { WorkerId = "test-worker-pod" } });
                while (await requestStream.MoveNext(context.CancellationToken))
                {
                    if (requestStream.Current.WorkerInitRequest is not null)
                    {
                        await responseStream.WriteAsync(new()
                        {
                            RequestId = requestStream.Current.RequestId,
                            WorkerInitResponse = new()
                            {
                                Result = status is { } value ? new()
                                {
                                    Status = value,
                                    Exception = new() { Message = "original-worker-initialization-error" }
                                }
                                    : null,
                                Capabilities =
                                {
                                    ["WorkerProxyAppToken"] = proof,
                                    ["UntrustedCapability"] = "must-not-be-published"
                                },
                                AppCapabilities = { ["UntrustedAppCapability"] = "must-not-be-published" }
                            }
                        });
                    }
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                _disconnected.TrySetResult();
            }
        }
    }

    private sealed class AdvertisedOriginService(string origin, byte[] appKey) : FunctionRpc.FunctionRpcBase
    {
        public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream,
            IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
        {
            await responseStream.WriteAsync(new() { StartStream = new() { WorkerId = "test-worker-pod" } });
            try
            {
                while (await requestStream.MoveNext(context.CancellationToken))
                {
                    if (requestStream.Current.WorkerInitRequest is not null)
                    {
                        await responseStream.WriteAsync(new()
                        {
                            RequestId = requestStream.Current.RequestId,
                            WorkerInitResponse = new()
                            {
                                Result = Success(),
                                Capabilities =
                                {
                                    ["HttpUri"] = origin,
                                    ["WorkerProxyAppToken"] = ProxyAuthentication.CreateAppProof(
                                        appKey, "test-app", "test-worker-pod", DateTimeOffset.UtcNow)
                                }
                            }
                        });
                    }
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
