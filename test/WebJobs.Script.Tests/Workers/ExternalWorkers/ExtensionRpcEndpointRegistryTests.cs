// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Azure.WebJobs.Rpc.Core.Internal;
using Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.ExternalWorkers;

public class ExtensionRpcEndpointRegistryTests
{
    private const string Method = "/extensions.Echo/Unary";

    [Fact]
    public async Task NewCallsRebindToReplacementScriptHost_WhileExistingCallRemainsPinned()
    {
        var registry = new ExtensionRpcEndpointRegistry();
        var manager = new ConnectedWorkerChannelManager(registry);
        Mock<IRpcWorkerChannel> firstChannel = CreateChannel("worker-1");
        manager.AddChannel("worker-1", firstChannel.Object);

        await using ServiceProvider firstServices = new ServiceCollection().BuildServiceProvider();
        using ScriptHostExtensionRpcEndpointCatalog firstCatalog = CreateCatalog(registry, firstServices, Method);
        await firstCatalog.StartAsync(CancellationToken.None);
        ExtensionRpcEndpoint existingEndpoint = Assert.IsType<ExtensionRpcEndpoint>(
            await registry.RouteAsync("worker-1", Method, CancellationToken.None));

        await using ServiceProvider secondServices = new ServiceCollection().BuildServiceProvider();
        using ScriptHostExtensionRpcEndpointCatalog secondCatalog = CreateCatalog(registry, secondServices, Method);
        await secondCatalog.StartAsync(CancellationToken.None);

        await using ExtensionRpcEndpoint replacementEndpoint = Assert.IsType<ExtensionRpcEndpoint>(
            await registry.RouteAsync("worker-1", Method, CancellationToken.None));

        Assert.Same(firstServices, existingEndpoint.Services);
        Assert.Same(secondServices, replacementEndpoint.Services);
        Assert.False(existingEndpoint.CancellationToken.IsCancellationRequested);

        Task firstStopTask = firstCatalog.StopAsync(CancellationToken.None);
        Assert.True(existingEndpoint.CancellationToken.IsCancellationRequested);
        Assert.False(firstStopTask.IsCompleted);

        await existingEndpoint.DisposeAsync();
        await firstStopTask;
        await replacementEndpoint.DisposeAsync();
        await secondCatalog.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_UnbindsWorkerAndWaitsForActiveCall()
    {
        var registry = new ExtensionRpcEndpointRegistry();
        var manager = new ConnectedWorkerChannelManager(registry);
        manager.AddChannel("worker-1", CreateChannel("worker-1").Object);
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        using ScriptHostExtensionRpcEndpointCatalog catalog = CreateCatalog(registry, services, Method);
        await catalog.StartAsync(CancellationToken.None);
        ExtensionRpcEndpoint endpoint = Assert.IsType<ExtensionRpcEndpoint>(
            await registry.RouteAsync("worker-1", Method, CancellationToken.None));

        Task stopTask = catalog.StopAsync(CancellationToken.None);

        Assert.Null(await registry.RouteAsync("worker-1", Method, CancellationToken.None));
        Assert.True(endpoint.CancellationToken.IsCancellationRequested);
        Assert.False(stopTask.IsCompleted);

        await endpoint.DisposeAsync();
        await stopTask;
    }

    [Fact]
    public async Task DrainBeforeReplacement_RebindsConnectedWorkerWhenReplacementRegisters()
    {
        var registry = new ExtensionRpcEndpointRegistry();
        var manager = new ConnectedWorkerChannelManager(registry);
        manager.AddChannel("worker-1", CreateChannel("worker-1").Object);
        await using ServiceProvider firstServices = new ServiceCollection().BuildServiceProvider();
        using ScriptHostExtensionRpcEndpointCatalog firstCatalog =
            CreateCatalog(registry, firstServices, Method);
        await firstCatalog.StartAsync(CancellationToken.None);

        await firstCatalog.StopAsync(CancellationToken.None);
        Assert.Null(await registry.RouteAsync("worker-1", Method, CancellationToken.None));

        await using ServiceProvider replacementServices = new ServiceCollection().BuildServiceProvider();
        using ScriptHostExtensionRpcEndpointCatalog replacementCatalog =
            CreateCatalog(registry, replacementServices, Method);
        await replacementCatalog.StartAsync(CancellationToken.None);

        await using ExtensionRpcEndpoint replacementEndpoint = Assert.IsType<ExtensionRpcEndpoint>(
            await registry.RouteAsync("worker-1", Method, CancellationToken.None));
        Assert.Same(replacementServices, replacementEndpoint.Services);

        await replacementEndpoint.DisposeAsync();
        await replacementCatalog.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EndpointChange_UpdatesOnlyOwningCatalog()
    {
        var registry = new ExtensionRpcEndpointRegistry();
        var manager = new ConnectedWorkerChannelManager(registry);
        var endpoints = new TestEndpointDataSource(CreateEndpoint(Method));
        manager.AddChannel("worker-1", CreateChannel("worker-1").Object);
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        using var catalog = new ScriptHostExtensionRpcEndpointCatalog(
            registry,
            services,
            [endpoints],
            NullLogger<ScriptHostExtensionRpcEndpointCatalog>.Instance);
        await catalog.StartAsync(CancellationToken.None);

        await using ExtensionRpcEndpoint originalEndpoint = Assert.IsType<ExtensionRpcEndpoint>(
            await registry.RouteAsync("worker-1", Method, CancellationToken.None));

        const string updatedMethod = "/extensions.Echo/Updated";
        endpoints.SetEndpoints(CreateEndpoint(updatedMethod));

        Assert.Null(await registry.RouteAsync("worker-1", Method, CancellationToken.None));
        await using ExtensionRpcEndpoint updatedEndpoint = Assert.IsType<ExtensionRpcEndpoint>(
            await registry.RouteAsync("worker-1", updatedMethod, CancellationToken.None));

        await originalEndpoint.DisposeAsync();
        await updatedEndpoint.DisposeAsync();
        await catalog.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WorkerShutdown_RemovesRoutingAssociation()
    {
        var registry = new ExtensionRpcEndpointRegistry();
        var manager = new ConnectedWorkerChannelManager(registry);
        manager.AddChannel("worker-1", CreateChannel("worker-1").Object);
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        using ScriptHostExtensionRpcEndpointCatalog catalog = CreateCatalog(registry, services, Method);
        await catalog.StartAsync(CancellationToken.None);
        await using ExtensionRpcEndpoint endpoint = Assert.IsType<ExtensionRpcEndpoint>(
            await registry.RouteAsync("worker-1", Method, CancellationToken.None));
        await endpoint.DisposeAsync();

        await manager.ShutdownChannelAsync("worker-1");

        Assert.Null(await registry.RouteAsync("worker-1", Method, CancellationToken.None));
        await catalog.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DisposedCatalog_RebindsWorkerWhenReplacementRegisters()
    {
        var registry = new ExtensionRpcEndpointRegistry();
        registry.BindWorker("worker-1");
        await using ServiceProvider firstServices = new ServiceCollection().BuildServiceProvider();
        using ScriptHostExtensionRpcEndpointCatalog firstCatalog = CreateCatalog(registry, firstServices, Method);
        await firstCatalog.StartAsync(CancellationToken.None);

        firstCatalog.Dispose();

        await using ServiceProvider replacementServices = new ServiceCollection().BuildServiceProvider();
        using ScriptHostExtensionRpcEndpointCatalog replacementCatalog =
            CreateCatalog(registry, replacementServices, Method);
        await replacementCatalog.StartAsync(CancellationToken.None);
        await using ExtensionRpcEndpoint endpoint = Assert.IsType<ExtensionRpcEndpoint>(
            await registry.RouteAsync("worker-1", Method, CancellationToken.None));

        Assert.Same(replacementServices, endpoint.Services);

        await endpoint.DisposeAsync();
        await replacementCatalog.StopAsync(CancellationToken.None);
    }

    private static ScriptHostExtensionRpcEndpointCatalog CreateCatalog(
        ExtensionRpcEndpointRegistry registry,
        IServiceProvider services,
        string method)
    {
        return new ScriptHostExtensionRpcEndpointCatalog(
            registry,
            services,
            [new TestEndpointDataSource(CreateEndpoint(method))],
            NullLogger<ScriptHostExtensionRpcEndpointCatalog>.Instance);
    }

    private static RouteEndpoint CreateEndpoint(string method)
    {
        return new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(method),
            0,
            EndpointMetadataCollection.Empty,
            method);
    }

    private static Mock<IRpcWorkerChannel> CreateChannel(string workerId)
    {
        var channel = new Mock<IRpcWorkerChannel>();
        channel.SetupGet(p => p.Id).Returns(workerId);
        channel.Setup(p => p.DrainInvocationsAsync()).Returns(Task.CompletedTask);
        return channel;
    }

    private sealed class TestEndpointDataSource(params Endpoint[] endpoints) : WebJobsRpcEndpointDataSource
    {
        private CancellationTokenSource _changeTokenSource = new();
        private IReadOnlyList<Endpoint> _endpoints = endpoints;

        public override IReadOnlyList<Endpoint> Endpoints => _endpoints;

        public override IChangeToken GetChangeToken()
        {
            return new CancellationChangeToken(_changeTokenSource.Token);
        }

        public void SetEndpoints(params Endpoint[] endpoints)
        {
            CancellationTokenSource previous = _changeTokenSource;
            _changeTokenSource = new CancellationTokenSource();
            _endpoints = endpoints;
            previous.Cancel();
            previous.Dispose();
        }
    }
}
