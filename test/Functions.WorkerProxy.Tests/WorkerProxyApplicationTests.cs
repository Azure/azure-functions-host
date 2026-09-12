// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class WorkerProxyApplicationTests
{
    [Fact]
    public async Task CapabilityFinalizer_ResolvesHttpProviderAsSingleton()
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        IWorkerCapabilityFinalizer finalizer = factory.Services.GetRequiredService<IWorkerCapabilityFinalizer>();
        using IServiceScope scope = factory.Services.CreateScope();

        Assert.IsType<WorkerHttpCapabilityProvider>(finalizer);
        Assert.Same(finalizer, scope.ServiceProvider.GetRequiredService<IWorkerCapabilityFinalizer>());
    }

    [Fact]
    public async Task StateManager_ResolvesAsSingletonWithConfiguredIdentity()
    {
        Dictionary<string, string?> configuration = new() { ["WorkerProxy:PodName"] = "configured-worker-pod" };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using IServiceScope scope = factory.Services.CreateScope();

        Assert.Same(TimeProvider.System, factory.Services.GetRequiredService<TimeProvider>());
        Assert.Same(manager, scope.ServiceProvider.GetRequiredService<WorkerPodStateManager>());
        Assert.Equal("configured-worker-pod", manager.State.PodName);
        Assert.Equal(0, manager.State.Revision);
    }

    [Fact]
    public async Task StateManager_UsesTimeProviderFromContainer()
    {
        Mock<TimeProvider> provider = new();
        Mock<ITimer> timer = new();
        TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        timer.Setup(instance => instance.Dispose()).Callback(() => disposed.TrySetResult());
        provider.Setup(clock => clock.CreateTimer(
            It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns(timer.Object);
        await using WorkerProxyWebApplicationFactory factory = new(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(provider.Object)));
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        Task<WorkerStatePollResult> poll = manager.WaitForChangeAsync(0, timeout.Token);

        Assert.False(poll.IsCompleted);
        provider.Verify(clock => clock.CreateTimer(
            It.IsAny<TimerCallback>(), It.IsAny<object?>(), TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan), Times.Once());
        manager.OnWorkerAttached(1);
        Assert.True((await poll).HasChanged);
        await disposed.Task.WaitAsync(timeout.Token);
        timer.Verify(instance => instance.Dispose(), Times.AtLeastOnce());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Startup_RejectsMissingOrBlankPodName(string? podName)
    {
        Dictionary<string, string?> configuration = new() { ["WorkerProxy:PodName"] = podName };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => factory.Services.GetRequiredService<WorkerPodStateManager>());

        Assert.Contains(exception.Failures, failure => failure.Contains(nameof(WorkerProxyOptions.PodName), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ManagementListener_ProtectsAdminRoutes()
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

        using HttpResponseMessage readyResponse = await client.GetAsync("/admin/instance/ready", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
        Assert.Empty(await readyResponse.Content.ReadAsByteArrayAsync(timeout.Token));

        using HttpResponseMessage unsupportedMethodResponse = await client.PostAsync("/admin/instance/ready", content: null, timeout.Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, unsupportedMethodResponse.StatusCode);

        using HttpResponseMessage unrelatedRouteResponse = await client.GetAsync("/admin/worker/unknown", timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, unrelatedRouteResponse.StatusCode);

        using HttpClient forwardingClient = factory.CreateHttpForwardingClient();
        using HttpResponseMessage forwardingReadyResponse =
            await forwardingClient.GetAsync("/admin/instance/ready", timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, forwardingReadyResponse.StatusCode);
    }
}
