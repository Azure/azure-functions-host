// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

extern alias workerproxy;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Azure.Functions.Host.Controllers;
using Azure.Functions.Host.WorkerLink;
using Azure.Functions.Rpc.Client;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using ProxyApplication = workerproxy::Azure.Functions.WorkerProxy.WorkerProxyApplication;
using ProxyEndpoints = workerproxy::Azure.Functions.WorkerProxy.WorkerProxyEndpointConfiguration;
using ProxyRelaySide = workerproxy::Azure.Functions.WorkerProxy.Rpc.FunctionRpcRelaySide;
using ProxyStateManager = workerproxy::Azure.Functions.WorkerProxy.State.WorkerPodStateManager;

namespace Azure.Functions.Host.Tests;

public class WorkerRuntimeAuthenticationIntegrationTests
{
    private const string WorkerTokenHeader = "x-ms-functions-worker-token";
    private const string AppTokenHeader = "x-ms-functions-app-token";
    private const string AppKeySetting = "WEBSITE_AUTH_ENCRYPTION_KEY";

    [Fact]
    public async Task LinkAndHttpInvocation_UseBothJwtsOverTlsThroughRealWorkerProxy()
    {
        const string workerId = "test-worker-pod";
        const string appName = "test-app";
        string appKey = Convert.ToHexString(SHA256.HashData("integration-test-app-key"u8));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        WebApplicationBuilder workerBuilder = WebApplication.CreateSlimBuilder();
        workerBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using WebApplication customerHttp = workerBuilder.Build();
        customerHttp.Run(async context =>
        {
            Assert.Equal("Bearer customer-token", context.Request.Headers.Authorization);
            Assert.False(context.Request.Headers.ContainsKey(WorkerTokenHeader));
            Assert.False(context.Request.Headers.ContainsKey(AppTokenHeader));
            string body = await new StreamReader(context.Request.Body).ReadToEndAsync(context.RequestAborted);
            await context.Response.WriteAsync($"received:{body}", context.RequestAborted);
        });
        await customerHttp.StartAsync(timeout.Token);
        string customerAddress = customerHttp.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        await using WebApplication proxy = ProxyApplication.Build(
        [
            "--WEBSITE_POD_NAME", workerId,
            "--FUNCTIONS_WORKER_RUNTIME_ACCESS_KEY", WorkerLinkTestData.RuntimeAccessKey,
            "--WorkerProxy:ManagementPort", "0",
            "--WorkerProxy:RuntimeGrpcPort", "0",
            "--WorkerProxy:WorkerGrpcPort", "0",
            "--WorkerProxy:HttpPort", "0",
            "--Logging:LogLevel:Default", "Warning"
        ]);
        await proxy.StartAsync(timeout.Token);
        ProxyEndpoints endpoints = proxy.Services.GetRequiredService<ProxyEndpoints>();
        Uri grpcEndpoint = Loopback(endpoints.GetRelayAddress(ProxyRelaySide.Runtime));
        Uri httpEndpoint = Loopback(endpoints.GetHttpAddress());
        Assert.Equal(Uri.UriSchemeHttps, grpcEndpoint.Scheme);
        Assert.Equal(Uri.UriSchemeHttps, httpEndpoint.Scheme);

        using GrpcChannel localWorkerChannel = GrpcChannel.ForAddress(Loopback(endpoints.GetRelayAddress(ProxyRelaySide.Worker)));
        FunctionRpc.FunctionRpcClient localWorker = new(localWorkerChannel);
        using AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> workerCall =
            localWorker.EventStream(cancellationToken: timeout.Token);
        await workerCall.RequestStream.WriteAsync(new StreamingMessage { StartStream = new() { WorkerId = workerId } }, timeout.Token);
        ProxyStateManager manager = proxy.Services.GetRequiredService<ProxyStateManager>();
        while (!manager.State.IsWorkerReady)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        using HttpClient management = new() { BaseAddress = Loopback(endpoints.GetManagementAddress()) };
        using HttpResponseMessage assignment = await HttpClientJsonExtensions.PutAsJsonAsync(management, "/admin/worker/assignment", new
        {
            startupMode = "Preconfigured",
            functionAppName = appName,
            functionGroupName = "group",
            isAlwaysReady = false,
            functionAppDirectory = string.Empty,
            environment = new Dictionary<string, string> { [AppKeySetting] = appKey }
        }, timeout.Token);
        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);

        await using ServiceProvider runtime = CreateRuntime(appName, appKey);
        IWorkerChannelRegistry registry = runtime.GetRequiredService<IWorkerChannelRegistry>();
        WorkerLinkController controller = new(NullLogger<WorkerLinkController>.Instance, registry);
        Task<IActionResult> linking = controller.LinkWorker(workerId, new WorkerLinkRequest
        {
            WorkerGrpcEndpoint = grpcEndpoint.AbsoluteUri,
            WorkerHttpEndpoint = httpEndpoint.AbsoluteUri,
            RuntimeAccessKey = WorkerLinkTestData.RuntimeAccessKey
        }, timeout.Token);
        StreamingMessage init = await ReadAsync(workerCall, timeout.Token);
        Assert.NotNull(init.WorkerInitRequest);
        await workerCall.RequestStream.WriteAsync(new StreamingMessage
        {
            RequestId = init.RequestId,
            WorkerInitResponse = new()
            {
                Result = Success(),
                Capabilities = { ["HttpUri"] = customerAddress }
            }
        }, timeout.Token);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<StatusCodeResult>(await linking).StatusCode);
        Assert.True(registry.TryGetInitializedChannel(workerId, out WorkerChannel channel));

        FunctionMetadata function = new() { Name = "Http", Language = "external" };
        function.Bindings.Add(new() { Name = "req", Type = "httpTrigger", Direction = BindingDirection.In });
        channel.SetupFunctionInvocationBuffers([function]);
        channel.SendFunctionLoadRequests(new ManagedDependencyOptions(), null);
        StreamingMessage load = await ReadAsync(workerCall, timeout.Token);
        Assert.NotNull(load.FunctionLoadRequest);
        await workerCall.RequestStream.WriteAsync(new StreamingMessage
        {
            RequestId = load.RequestId,
            FunctionLoadResponse = new() { FunctionId = function.GetFunctionId(), Result = Success() }
        }, timeout.Token);

        DefaultHttpContext httpContext = new();
        using MemoryStream requestBody = new(Encoding.UTF8.GetBytes("payload"));
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
        Assert.True(await channel.FunctionInputBuffers[function.GetFunctionId()].SendAsync(invocation, timeout.Token));
        StreamingMessage invoked = await ReadAsync(workerCall, timeout.Token);
        Assert.Equal(new RpcHttp(), Assert.Single(invoked.InvocationRequest.InputData).Data.Http);
        await workerCall.RequestStream.WriteAsync(new StreamingMessage
        {
            RequestId = invoked.RequestId,
            InvocationResponse = new() { InvocationId = invoked.InvocationRequest.InvocationId, Result = Success() }
        }, timeout.Token);

        await invocation.ResultSource.Task.WaitAsync(timeout.Token);
        Assert.Equal("received:payload", Encoding.UTF8.GetString(responseBody.ToArray()));
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        string state = await management.GetStringAsync("/admin/worker/state", timeout.Token);
        Assert.DoesNotContain(appKey, state, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkerLinkTestData.RuntimeAccessKey, state, StringComparison.Ordinal);
    }

    private static ServiceProvider CreateRuntime(string appName, string appKey)
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
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WEBSITE_SITE_NAME"] = appName,
            [AppKeySetting] = appKey
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
}
