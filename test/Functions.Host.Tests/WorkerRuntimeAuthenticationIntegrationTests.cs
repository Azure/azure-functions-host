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
using ProxyEndpoints = workerproxy::Azure.Functions.WorkerProxy.WorkerProxyEndpointConfiguration;
using ProxyRelaySide = workerproxy::Azure.Functions.WorkerProxy.Rpc.FunctionRpcRelaySide;
using ProxyStateManager = workerproxy::Azure.Functions.WorkerProxy.State.WorkerPodStateManager;

namespace Azure.Functions.Host.Tests;

public class WorkerRuntimeAuthenticationIntegrationTests(ITestOutputHelper output)
{
    private const string WorkerTokenHeader = "x-ms-functions-worker-token";
    private const string AppTokenHeader = "x-ms-functions-app-token";
    private const string AppKeySetting = "WEBSITE_AUTH_ENCRYPTION_KEY";

    [Theory]
    [InlineData("valid")]
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
            "--CONTAINER_ENCRYPTION_KEY", WorkerLinkTestData.RuntimeAccessKey,
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
            ManagementTokenTestData.Create(WorkerLinkTestData.RuntimeAccessKey, workerId));
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
            {"workerPodName":"test-worker-pod","workerGrpcEndpoint":"{{grpcEndpoint.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped)}}","runtimeAccessKey":"{{WorkerLinkTestData.RuntimeAccessKey}}"}
            """;
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        Task<HttpResponseMessage> linking = managementReceiver.Client.PutAsync(
            $"/admin/workers/{Uri.EscapeDataString(workerId)}", content, timeout.Token);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.False(linking.IsCompleted);

        using GrpcChannel localWorkerChannel = GrpcChannel.ForAddress(
            endpoints is null ? NativeAddress("WORKER_GRPC") : Loopback(endpoints.GetRelayAddress(ProxyRelaySide.Worker)));
        FunctionRpc.FunctionRpcClient localWorker = new(localWorkerChannel);
        using AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> workerCall =
            localWorker.EventStream(cancellationToken: timeout.Token);
        await workerCall.RequestStream.WriteAsync(new StreamingMessage { StartStream = new() { WorkerId = workerId } }, timeout.Token);
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
        using HttpResponseMessage linkResponse = await linking;
        if (!string.Equals(scenario, "valid", StringComparison.Ordinal))
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
        Assert.DoesNotContain(WorkerLinkTestData.RuntimeAccessKey, state, StringComparison.Ordinal);
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
        services.AddRpcClientServices();
        ServiceDescriptor forwarding = services.Single(service => service.ServiceType == typeof(IHttpProxyService));
        services.Replace(ServiceDescriptor.Singleton<IHttpProxyService>(provider =>
            new RecordingHttpProxyService((IHttpProxyService)forwarding.ImplementationFactory!(provider), forwardedOrigins)));

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

    private sealed class RecordingHttpProxyService(IHttpProxyService inner, ConcurrentQueue<Uri> origins) : IHttpProxyService, IDisposable
    {
        public void StartForwarding(ScriptInvocationContext context, Uri httpUri)
        {
            origins.Enqueue(httpUri);
            inner.StartForwarding(context, httpUri);
        }

        public Task EnsureSuccessfulForwardingAsync(ScriptInvocationContext context) => inner.EnsureSuccessfulForwardingAsync(context);

        public void Dispose()
        {
            if (inner is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
