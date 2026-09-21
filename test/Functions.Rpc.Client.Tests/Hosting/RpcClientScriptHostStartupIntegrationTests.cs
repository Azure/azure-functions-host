// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Composition;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class RpcClientScriptHostStartupIntegrationTests
{
    private const string HostConfiguration = """{"version":"2.0","isDefaultHostConfig":true}""";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task FirstLink_IndexesWorkerMetadataWithExistingHostConfiguration_AndLaterLinksDoNotRestart()
    {
        await using TestWorker firstWorker = await TestWorker.StartAsync("first");
        await using TestWorker laterWorker = await TestWorker.StartAsync("later");
        using CancellationTokenSource timeout = new(TestTimeout);

        await using (ClientTestHost testHost = new())
        {
            await testHost.Host.StartAsync(timeout.Token);
            IWorkerChannelRegistry registry = testHost.Host.Services.GetRequiredService<IWorkerChannelRegistry>();
            RpcClientScriptHostStartupCoordinator coordinator = testHost.Host.Services.GetRequiredService<RpcClientScriptHostStartupCoordinator>();
            IScriptHostManager manager = testHost.Host.Services.GetRequiredService<IScriptHostManager>();

            Assert.Null(manager.Services);
            Assert.Equal(ScriptHostState.Default, manager.State);
            Assert.False(coordinator.ActivationTask!.IsCompleted);
            Assert.Equal("host.json", Path.GetFileName(Assert.Single(Directory.GetFiles(testHost.ScriptPath))));
            Assert.DoesNotContain(testHost.Host.Services.GetServices<IHostedService>(), service => service is WebJobsScriptHostService);

            WorkerChannel firstChannel = await registry.LinkAsync("first", firstWorker.Endpoint, timeout.Token);
            await coordinator.ActivationTask!.WaitAsync(timeout.Token);
            await WaitForHostStateAsync(manager, ScriptHostState.Running, timeout.Token);
            await firstWorker.Service.FunctionLoaded.Task.WaitAsync(timeout.Token);

            Assert.Equal(ScriptHostState.Running, manager.State);
            IServiceProvider scriptServices = Assert.IsAssignableFrom<IServiceProvider>(manager.Services);
            ScriptHost scriptHost = scriptServices.GetRequiredService<ScriptHost>();
            FunctionDescriptor function = Assert.Single(scriptHost.Functions);
            Assert.Equal("Http", function.Name);
            Assert.Equal(AuthorizationLevel.Anonymous, function.HttpTriggerAttribute.AuthLevel);
            var logCalls = testHost.Logger.Invocations
                .Where(invocation => string.Equals(invocation.Method.Name, nameof(ILogger.Log), StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(logCalls);
            Assert.DoesNotContain(logCalls, invocation => invocation.Arguments[1] is EventId { Id: 340 });
            Assert.True(scriptServices.GetRequiredService<IOptions<ScriptJobHostOptions>>().Value.IsDefaultHostConfig);
            Assert.Same(registry, scriptServices.GetRequiredService<IWorkerChannelRegistry>());
            Assert.Same(testHost.Host.Services.GetRequiredService<IWorkerFunctionMetadataProvider>(),
                scriptServices.GetRequiredService<IWorkerFunctionMetadataProvider>());
            Assert.Equal(1, firstWorker.Service.MetadataRequests);
            Assert.Equal(HostConfiguration, File.ReadAllText(Path.Combine(testHost.ScriptPath, "host.json")));
            Assert.Equal("Files", testHost.Configuration[EnvironmentSettingNames.AzureWebJobsSecretStorageType]);
            Assert.True(testHost.Host.Services.GetRequiredService<ISecretManagerProvider>().SecretsEnabled);

            await registry.LinkAsync("later", laterWorker.Endpoint, timeout.Token);

            Assert.Same(scriptServices, manager.Services);
            Assert.Same(scriptHost, scriptServices.GetRequiredService<ScriptHost>());
            Assert.Equal(2, registry.GetInitializedChannels().Count);
            Assert.Equal(1, firstWorker.Service.MetadataRequests);
            Assert.Equal(0, laterWorker.Service.MetadataRequests);

            await coordinator.StopAsync(timeout.Token);

            Assert.Equal(ScriptHostState.Stopped, manager.State);
            Assert.Null(manager.Services);
            Assert.True(registry.TryGetInitializedChannel("first", out WorkerChannel retainedChannel));
            Assert.Same(firstChannel, retainedChannel);
        }

        await firstWorker.Service.Disconnected.Task.WaitAsync(timeout.Token);
        await laterWorker.Service.Disconnected.Task.WaitAsync(timeout.Token);
    }

    [Theory]
    [InlineData("host.json", "{invalid json", ScriptHostState.Error)]
    [InlineData("app_offline.htm", "offline", ScriptHostState.Offline)]
    public async Task FirstLink_LeavesConfigurationErrorsAndOfflineHandlingWithTheManager(
        string fileName, string content, ScriptHostState expectedState)
    {
        await using TestWorker worker = await TestWorker.StartAsync("worker");
        await using ClientTestHost testHost = new();
        using CancellationTokenSource timeout = new(TestTimeout);
        File.WriteAllText(Path.Combine(testHost.ScriptPath, fileName), content);
        await testHost.Host.StartAsync(timeout.Token);
        IWorkerChannelRegistry registry = testHost.Host.Services.GetRequiredService<IWorkerChannelRegistry>();
        RpcClientScriptHostStartupCoordinator coordinator = testHost.Host.Services.GetRequiredService<RpcClientScriptHostStartupCoordinator>();
        IScriptHostManager manager = testHost.Host.Services.GetRequiredService<IScriptHostManager>();

        WorkerChannel channel = await registry.LinkAsync("worker", worker.Endpoint, timeout.Token);
        await coordinator.ActivationTask!.WaitAsync(timeout.Token);
        await WaitForHostStateAsync(manager, expectedState, timeout.Token);
        Task activation = coordinator.ActivationTask!;
        await coordinator.StartAsync(timeout.Token);

        Assert.Same(activation, coordinator.ActivationTask);
        Assert.True(activation.IsCompletedSuccessfully);
        Assert.Equal(expectedState, manager.State);
        if (expectedState is ScriptHostState.Error)
        {
            Assert.IsType<FormatException>(manager.LastError);
        }
        else
        {
            Assert.Null(manager.LastError);
        }

        Assert.False(testHost.Host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        Assert.True(registry.TryGetInitializedChannel("worker", out WorkerChannel retainedChannel));
        Assert.Same(channel, retainedChannel);
        Assert.Equal(0, worker.Service.MetadataRequests);
    }

    [Fact]
    public async Task FirstLink_TransientHostBuildFailure_IsRetriedByTheExistingManager()
    {
        await using TestWorker worker = await TestWorker.StartAsync("worker");
        InvalidOperationException failure = new("Transient host build failure.");
        var configureBuilder = new Mock<IConfigureBuilder<IServiceCollection>>();
        configureBuilder.SetupSequence(builder => builder.Configure(It.IsAny<IServiceCollection>()))
            .Throws(failure)
            .Pass();
        await using ClientTestHost testHost = new(services => services.AddSingleton(configureBuilder.Object));
        using CancellationTokenSource timeout = new(TestTimeout);
        await testHost.Host.StartAsync(timeout.Token);
        IWorkerChannelRegistry registry = testHost.Host.Services.GetRequiredService<IWorkerChannelRegistry>();
        RpcClientScriptHostStartupCoordinator coordinator = testHost.Host.Services.GetRequiredService<RpcClientScriptHostStartupCoordinator>();
        IScriptHostManager manager = testHost.Host.Services.GetRequiredService<IScriptHostManager>();

        await registry.LinkAsync("worker", worker.Endpoint, timeout.Token);
        await coordinator.ActivationTask!.WaitAsync(timeout.Token);
        await WaitForHostStateAsync(manager, ScriptHostState.Running, timeout.Token);
        Task activation = coordinator.ActivationTask!;
        await coordinator.StartAsync(timeout.Token);

        Assert.Same(activation, coordinator.ActivationTask);
        Assert.Null(manager.LastError);
        Assert.Single(manager.Services!.GetRequiredService<ScriptHost>().Functions);
        configureBuilder.Verify(builder => builder.Configure(It.IsAny<IServiceCollection>()), Times.Exactly(2));
        Assert.True(registry.TryGetInitializedChannel("worker", out _));
    }

    private static async Task WaitForHostStateAsync(IScriptHostManager manager, ScriptHostState state, CancellationToken cancellationToken)
    {
        while (manager.State != state)
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed class ClientTestHost : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ClientHost-{Guid.NewGuid():N}");

        public ClientTestHost(Action<IServiceCollection>? configureServices = null)
        {
            ScriptPath = Path.Combine(_directory, "app");
            Directory.CreateDirectory(ScriptPath);
            File.WriteAllText(Path.Combine(ScriptPath, "host.json"), HostConfiguration);
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EnvironmentSettingNames.FunctionWorkerRuntime] = "dotnet-isolated",
                [EnvironmentSettingNames.AzureWebJobsSecretStorageType] = "Files",
                ["AzureFunctionsWebHost:ScriptPath"] = ScriptPath,
                ["AzureFunctionsWebHost:LogPath"] = Path.Combine(_directory, "logs"),
                ["AzureFunctionsWebHost:SecretsPath"] = Path.Combine(_directory, "secrets"),
                ["AzureFunctionsWebHost:IsSelfHost"] = "true",
            }).Build();
            var environment = new Mock<IEnvironment>();
            environment.Setup(value => value.GetEnvironmentVariable(It.IsAny<string>())).Returns((string name) => Configuration[name]!);
            Logger.Setup(logger => logger.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            var loggerProvider = new Mock<ILoggerProvider>();
            loggerProvider.Setup(provider => provider.CreateLogger(It.IsAny<string>())).Returns(Logger.Object);
            var composition = new Mock<IWorkerComposition>();
            composition.Setup(value => value.ConfigureWebHostServices(It.IsAny<IServiceCollection>(), It.IsAny<IMvcBuilder>()))
                .Callback<IServiceCollection, IMvcBuilder>((services, _) =>
                {
                    services.AddRpcClientWebHostServices(static provider => provider.GetRequiredService<WebJobsScriptHostService>());
                    services.AddSingleton(Mock.Of<IWebHostWorkerManager>());
                });
            composition.Setup(value => value.ConfigureScriptHostServices(It.IsAny<IServiceCollection>(), It.IsAny<IServiceProvider>()))
                .Callback<IServiceCollection, IServiceProvider>((services, provider) => services.AddRpcClientScriptHostServices(provider));

            Host = new HostBuilder().ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls("http://127.0.0.1:0");
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton(Configuration);
                    services.AddSingleton(environment.Object);
                    services.AddWebJobsScriptHost(Configuration, composition.Object);
                    services.AddSingleton<IConfigureBuilder<ILoggingBuilder>>(new TestLogging(loggerProvider.Object));
                    configureServices?.Invoke(services);
                });
                webBuilder.Configure(_ => { });
            }).Build();
        }

        public string ScriptPath { get; }

        public IConfiguration Configuration { get; }

        public IHost Host { get; }

        public Mock<ILogger> Logger { get; } = new();

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Host.StopAsync().WaitAsync(TestTimeout);
            }
            finally
            {
                await ((IAsyncDisposable)Host).DisposeAsync();
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private sealed class TestLogging(ILoggerProvider loggerProvider) : IConfigureBuilder<ILoggingBuilder>
    {
        public void Configure(ILoggingBuilder builder) => builder.AddProvider(loggerProvider);
    }

    private sealed class TestWorker(WebApplication application, Uri endpoint, TestWorkerService service) : IAsyncDisposable
    {
        public Uri Endpoint { get; } = endpoint;

        public TestWorkerService Service { get; } = service;

        public static async Task<TestWorker> StartAsync(string workerId)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(IPAddress.Loopback, 0, listener => listener.Protocols = HttpProtocols.Http2));
            TestWorkerService service = new(workerId);
            builder.Services.AddSingleton(service);
            builder.Services.AddGrpc();
            WebApplication application = builder.Build();
            application.MapGrpcService<TestWorkerService>();
            await application.StartAsync();
            IServer server = application.Services.GetRequiredService<IServer>();
            string address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();

            return new(application, new Uri(address), service);
        }

        public async ValueTask DisposeAsync()
        {
            await application.StopAsync();
            await application.DisposeAsync();
        }
    }

    public sealed class TestWorkerService(string workerId) : FunctionRpc.FunctionRpcBase
    {
        private int _metadataRequests;

        public int MetadataRequests => Interlocked.CompareExchange(ref _metadataRequests, 0, 0);

        public TaskCompletionSource FunctionLoaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream,
            IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
        {
            try
            {
                await responseStream.WriteAsync(new() { StartStream = new() { WorkerId = workerId } });
                while (await requestStream.MoveNext(context.CancellationToken))
                {
                    StreamingMessage request = requestStream.Current;
                    StreamingMessage response = new() { RequestId = request.RequestId };
                    switch (request.ContentCase)
                    {
                        case StreamingMessage.ContentOneofCase.WorkerInitRequest:
                            response.WorkerInitResponse = new() { Result = Success() };
                            break;
                        case StreamingMessage.ContentOneofCase.FunctionsMetadataRequest:
                            Interlocked.Increment(ref _metadataRequests);
                            response.FunctionMetadataResponse = new() { Result = Success() };
                            response.FunctionMetadataResponse.FunctionMetadataResults.Add(new RpcFunctionMetadata
                            {
                                FunctionId = "http",
                                Name = "Http",
                                Language = "dotnet-isolated",
                                ScriptFile = "Sample.dll",
                                EntryPoint = "Sample.Http.Run",
                                Status = Success(),
                                RawBindings =
                                {
                                    """{"name":"req","type":"httpTrigger","direction":"in","authLevel":"anonymous","methods":["get"]}""",
                                    """{"name":"$return","type":"http","direction":"out"}""",
                                },
                            });
                            break;
                        case StreamingMessage.ContentOneofCase.FunctionLoadRequest:
                            response.FunctionLoadResponse = new() { FunctionId = request.FunctionLoadRequest.FunctionId, Result = Success() };
                            FunctionLoaded.TrySetResult();
                            break;
                        case StreamingMessage.ContentOneofCase.WorkerStatusRequest:
                            response.WorkerStatusResponse = new();
                            break;
                        default:
                            throw new InvalidOperationException($"Unexpected worker request: {request.ContentCase}.");
                    }

                    await responseStream.WriteAsync(response);
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                Disconnected.TrySetResult();
            }
        }

        private static StatusResult Success() => new() { Status = StatusResult.Types.Status.Success };
    }
}
