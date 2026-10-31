// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Host.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    [Fact]
    public async Task PostScaleIn_AbsoluteRetainedCount_SelectsLowestWorkerIdAndRepeatIsNoOp()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        ChannelControl higherId = CreateClassifiedWorker("worker-z");
        ChannelControl lowerId = CreateClassifiedWorker("worker-a");
        await LinkAndReadyWorkersAsync(harness, registry, function, higherId, lowerId);
        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);

        using HttpResponseMessage retained = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":2}""");
        using HttpResponseMessage reduced = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":1}""");
        using HttpResponseMessage repeated = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":1}""");

        Assert.Equal(0, await ReadNewlyDrainingWorkerCountAsync(retained));
        Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(reduced));
        Assert.Equal(0, await ReadNewlyDrainingWorkerCountAsync(repeated));
        await lowerId.Transport.DisposeStarted.WaitAsync(TestTimeout);
        Assert.Equal(0, higherId.DisposeCount);
        Assert.Same(higherId.Channel, Assert.Single(registry.GetInitializedChannels()));
    }

    [Fact]
    public async Task PostScaleIn_ExactGroupClassAndUsablePopulation_ExcludesOtherAndDrainingWorkers()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        ChannelControl firstMatch = CreateClassifiedWorker("first-match");
        ChannelControl secondMatch = CreateClassifiedWorker("second-match");
        ChannelControl wrongCaseGroup = CreateClassifiedWorker("wrong-group", functionGroup: "Durable");
        ChannelControl wrongClass = CreateClassifiedWorker("wrong-class", isAlwaysReady: true);
        ChannelControl starting = new(
            "starting",
            blockStart: true,
            functionGroupName: FunctionGroups.Durable,
            isAlwaysReady: false);
        await LinkAndReadyWorkersAsync(
            harness,
            registry,
            function,
            firstMatch,
            secondMatch,
            wrongCaseGroup,
            wrongClass);
        harness.Enqueue(starting);
        Task<WorkerLinkResult> startingLink = registry.LinkAsync(starting.Id, CreateEndpoint(starting.Id));
        await starting.StartEntered.WaitAsync(TestTimeout);

        ChannelControl readinessPending = new(
            "readiness-pending",
            setupFailureCount: 1,
            functionGroupName: FunctionGroups.Durable,
            isAlwaysReady: false);
        harness.Enqueue(readinessPending);
        await LinkAsync(registry, readinessPending.Id);
        await readinessPending.SetupEntered.WaitAsync(TestTimeout);

        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);
        using HttpResponseMessage first = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":1}""");
        using HttpResponseMessage second = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":0}""");

        Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(first));
        Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(second));
        await firstMatch.Transport.DisposeStarted.WaitAsync(TestTimeout);
        await secondMatch.Transport.DisposeStarted.WaitAsync(TestTimeout);
        Assert.Equal(0, wrongCaseGroup.DisposeCount);
        Assert.Equal(0, wrongClass.DisposeCount);
        Assert.Equal(0, readinessPending.DisposeCount);

        starting.AllowStart();
        await startingLink.WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostScaleIn_SelectedBusyWorkerLogsWhetherRuntimeLinkWasClosed(bool unlinkBeforeDrainCompletes)
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        ChannelControl retained = CreateClassifiedWorker("worker-z");
        ChannelControl selected = CreateClassifiedWorker("worker-a");
        await LinkAndReadyWorkersAsync(harness, registry, function, retained, selected);

        ScriptInvocationContext committed = CreateScaleInInvocation(function);
        Assert.True(selected.Channel.TryDispatchInvocation(committed));
        StreamingMessage request = await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);

        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);
        using HttpResponseMessage response = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":1}""");

        Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(response));
        Assert.False(committed.ResultSource.Task.IsCompleted);
        Assert.False(selected.Transport.DisposeStarted.IsCompleted);
        Assert.False(selected.Channel.TryDispatchInvocation(CreateScaleInInvocation(function)));
        Assert.True(retained.Channel.IsReadyForInvocations);

        if (unlinkBeforeDrainCompletes)
        {
            Assert.True(await registry.UnlinkAsync(selected.Id));
            committed.ResultSource.TrySetCanceled();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => committed.ResultSource.Task.WaitAsync(TestTimeout));
        }
        else
        {
            await selected.Transport.SendResponseAsync(new()
            {
                InvocationResponse = new()
                {
                    InvocationId = request.InvocationRequest.InvocationId,
                    Result = new()
                    {
                        Status = StatusResult.Types.Status.Failure,
                        Exception = new()
                        {
                            Message = "Invocation failed.",
                        },
                    },
                },
            });
            await Assert.ThrowsAsync<Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException>(
                () => committed.ResultSource.Task.WaitAsync(TestTimeout));
            await selected.Transport.DisposeStarted.WaitAsync(TestTimeout);
        }

        int expectedEventId = unlinkBeforeDrainCompletes ? 8 : 6;
        await WaitUntilAsync(() => HasInformationLog(harness.RegistryLogger, expectedEventId));
    }

    [Fact]
    public async Task PostScaleIn_ScaleInWinsStaleSnapshotRaceAndDispatcherUsesRetainedWorker()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        ChannelControl selected = CreateClassifiedWorker("worker-a");
        ChannelControl retained = CreateClassifiedWorker("worker-z");
        await LinkAndReadyWorkersAsync(harness, registry, function, selected, retained);
        IReadOnlyList<WorkerChannel> staleReadyChannels = await registry.GetReadyChannelsAsync();
        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);

        using HttpResponseMessage response = await host.PostAsync(
            """{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":1}""");

        Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(response));
        await selected.Transport.DisposeStarted.WaitAsync(TestTimeout);

        Mock<IWorkerChannelRegistry> staleRegistry = new(MockBehavior.Strict);
        staleRegistry
            .Setup(registry => registry.GetReadyChannelsAsync())
            .Returns(new ValueTask<IReadOnlyList<WorkerChannel>>(staleReadyChannels));
        using RpcClientFunctionInvocationDispatcher dispatcher = new(
            staleRegistry.Object,
            NullLogger<RpcClientFunctionInvocationDispatcher>.Instance);
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext invocation = CreateScaleInInvocation(function);

        await dispatcher.InvokeAsync(invocation);

        StreamingMessage request = await retained.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await retained.Transport.SendResponseAsync(new()
        {
            InvocationResponse = new()
            {
                InvocationId = request.InvocationRequest.InvocationId,
                Result = new() { Status = StatusResult.Types.Status.Success },
            },
        });
        await invocation.ResultSource.Task.WaitAsync(TestTimeout);
        Assert.False(selected.Transport.Requests.TryRead(out _));
    }

    [Theory]
    [InlineData("""{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":0}""", false, HttpStatusCode.Unauthorized, null)]
    [InlineData("""{"functionGroup":"http","isAlwaysReady":false,"desiredWorkerCount":0}""", true, HttpStatusCode.BadRequest, "functionGroup")]
    [InlineData("""{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":-1}""", true, HttpStatusCode.BadRequest, "desiredWorkerCount")]
    [InlineData("""{"isAlwaysReady":false,"desiredWorkerCount":0}""", true, HttpStatusCode.BadRequest, "functionGroup")]
    [InlineData("""{"functionGroup":"durable","desiredWorkerCount":0}""", true, HttpStatusCode.BadRequest, "isAlwaysReady")]
    [InlineData("""{"functionGroup":"durable","isAlwaysReady":false}""", true, HttpStatusCode.BadRequest, "desiredWorkerCount")]
    [InlineData("""{"functionGroup":"durable","isAlwaysReady":false,"desiredWorkerCount":"invalid"}""", true, HttpStatusCode.BadRequest, "request")]
    public async Task PostScaleIn_InvalidHttpAndAuthenticationFailuresFailClosed(
        string json,
        bool includeSiteToken,
        HttpStatusCode expectedStatus,
        string expectedTarget)
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry);

        using HttpResponseMessage response = await host.PostAsync(json, includeSiteToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedTarget is not null)
        {
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement error = Assert.Single(body.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal(expectedTarget, error.GetProperty("target").GetString());
        }

        Assert.Empty(registry.GetInitializedChannels());
    }

    private static ChannelControl CreateClassifiedWorker(
        string id,
        string functionGroup = FunctionGroups.Durable,
        bool isAlwaysReady = false)
        => new(id, functionGroupName: functionGroup, isAlwaysReady: isAlwaysReady);

    private static async Task LinkAndReadyWorkersAsync(
        RegistryHarness harness,
        WorkerChannelRegistry registry,
        FunctionMetadata function,
        params ChannelControl[] workers)
    {
        foreach (ChannelControl worker in workers)
        {
            harness.Enqueue(worker);
            await LinkAsync(registry, worker.Id);
        }

        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
        foreach (ChannelControl worker in workers)
        {
            await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
            await worker.Transport.SendResponseAsync(new()
            {
                FunctionLoadResponse = new()
                {
                    FunctionId = function.GetFunctionId(),
                    Result = new() { Status = StatusResult.Types.Status.Success },
                },
            });
        }

        await WaitUntilAsync(() => workers.All(worker => worker.Channel.IsReadyForInvocations));
    }

    private static ScriptInvocationContext CreateScaleInInvocation(FunctionMetadata function)
        => new()
        {
            FunctionMetadata = function,
            ExecutionContext = new()
            {
                FunctionName = function.Name,
                InvocationId = Guid.NewGuid(),
            },
            BindingData = [],
            Inputs = [],
            ResultSource = new(TaskCreationOptions.RunContinuationsAsynchronously),
            CancellationToken = CancellationToken.None,
            AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),
            Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
        };

    private static async Task<int> ReadNewlyDrainingWorkerCountAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("newlyDrainingWorkerCount").GetInt32();
    }

    private static bool HasInformationLog(
        Mock<ILogger<WorkerChannelRegistry>> logger,
        int expectedEventId)
        => logger.Invocations.Any(invocation =>
            invocation.Arguments.Count > 1 &&
            invocation.Arguments[0] is LogLevel.Information &&
            invocation.Arguments[1] is EventId eventId &&
            eventId.Id == expectedEventId);

    private sealed class ScaleInApiHost : IAsyncDisposable
    {
        private const string AuthenticationScheme = "SiteToken";
        private const string TestSiteToken = "test-site-token";
        private readonly IHost _host;

        private ScaleInApiHost(IHost host)
        {
            _host = host;
            Client = host.GetTestClient();
        }

        internal HttpClient Client { get; }

        internal static async Task<ScaleInApiHost> StartAsync(IWorkerChannelRegistry registry)
        {
            IHost host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseSetting(WebHostDefaults.ApplicationKey, typeof(Startup).Assembly.GetName().Name)
                        .UseTestServer()
                        .ConfigureLogging(logging => logging.ClearProviders())
                        .ConfigureServices(services =>
                        {
                            IMvcBuilder mvcBuilder = services.AddMvc(options =>
                            {
                                options.EnableEndpointRouting = false;
                            }).AddNewtonsoftJson();
                            mvcBuilder.ConfigureApplicationPartManager(parts =>
                            {
                                parts.ApplicationParts.Clear();
                                parts.ApplicationParts.Add(new AssemblyPart(typeof(WorkerScaleInController).Assembly));
                            });

                            services.AddSingleton(registry);
                            services
                                .AddAuthentication(AuthenticationScheme)
                                .AddScheme<AuthenticationSchemeOptions, SiteTokenAuthenticationHandler>(
                                    AuthenticationScheme,
                                    _ => { });
                            services.AddAuthorization(options =>
                            {
                                options.AddPolicy(
                                    PolicyNames.AdminAuthLevel,
                                    policy =>
                                    {
                                        policy.AddAuthenticationSchemes(AuthenticationScheme);
                                        policy.RequireAuthenticatedUser();
                                    });
                            });
                        })
                        .Configure(app =>
                        {
                            app.UseAuthentication();
                            app.UseAuthorization();
                            app.UseMvc();
                        });
                })
                .Build();

            try
            {
                await host.StartAsync();
                return new(host);
            }
            catch
            {
                await DisposeHostAsync(host);
                throw;
            }
        }

        internal async Task<HttpResponseMessage> PostAsync(string json, bool includeSiteToken = true)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "/admin/workers/scale-in");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (includeSiteToken)
            {
                request.Headers.Add(ScriptConstants.SiteTokenHeaderName, TestSiteToken);
            }

            return await Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            using CancellationTokenSource timeout = new(TestTimeout);
            try
            {
                await _host.StopAsync(timeout.Token);
            }
            finally
            {
                await DisposeHostAsync(_host);
            }
        }

        private static async Task DisposeHostAsync(IHost host)
        {
            if (host is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            }
            else
            {
                host.Dispose();
            }
        }

        private sealed class SiteTokenAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
        {
            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                if (!Request.Headers.TryGetValue(ScriptConstants.SiteTokenHeaderName, out var values) ||
                    !string.Equals(values.ToString(), TestSiteToken, StringComparison.Ordinal))
                {
                    return Task.FromResult(AuthenticateResult.Fail("A valid site token is required."));
                }

                ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, "platform")], Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
            }
        }
    }
}
