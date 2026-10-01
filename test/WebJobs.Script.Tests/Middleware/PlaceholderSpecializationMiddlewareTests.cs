// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware;

public class PlaceholderSpecializationMiddlewareTests
{
    private const string ExemptPath = "/admin/workers/worker-pod-1";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly Mock<IScriptWebHostEnvironment> _webHostEnvironment = new();
    private readonly Mock<IStandbyManager> _standbyManager = new(MockBehavior.Strict);
    private readonly Mock<ISpecializationWaitExemption> _exemption = new();
    private readonly TestEnvironment _environment = new();
    private readonly TaskCompletionSource _specialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _nextCalls;

    public PlaceholderSpecializationMiddlewareTests()
    {
        _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
        _standbyManager.Setup(manager => manager.SpecializeHostAsync()).Returns(_specialization.Task);
    }

    [Fact]
    public async Task Invoke_InStandbyMode_ContinuesWithoutSpecializing()
    {
        _webHostEnvironment.SetupGet(environment => environment.InStandbyMode).Returns(true);

        await CreateMiddleware().Invoke(CreateContext(HttpMethods.Get, "/api/function"));

        Assert.Equal(1, _nextCalls);
        _standbyManager.Verify(manager => manager.SpecializeHostAsync(), Times.Never);
    }

    [Fact]
    public async Task Invoke_ContainerNotReady_ContinuesWithoutSpecializing()
    {
        _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, null);

        await CreateMiddleware().Invoke(CreateContext(HttpMethods.Get, "/api/function"));

        Assert.Equal(1, _nextCalls);
        _standbyManager.Verify(manager => manager.SpecializeHostAsync(), Times.Never);
    }

    [Fact]
    public async Task Invoke_Specializing_HoldsRequestUntilSpecializationCompletes()
    {
        Task request = CreateMiddleware().Invoke(CreateContext(HttpMethods.Get, "/api/function"));

        Assert.False(request.IsCompleted);
        Assert.Equal(0, _nextCalls);
        _specialization.SetResult();
        await request.WaitAsync(TestTimeout);
        Assert.Equal(1, _nextCalls);
        _standbyManager.Verify(manager => manager.SpecializeHostAsync(), Times.Once);
    }

    [Fact]
    public async Task Invoke_ExemptRequestDuringSpecialization_ContinuesWithoutWaiting()
    {
        _exemption.Setup(exemption => exemption.IsMatch(It.Is<HttpRequest>(request => request.Path == ExemptPath))).Returns(true);
        PlaceholderSpecializationMiddleware middleware = CreateMiddleware(_exemption.Object);

        await middleware.Invoke(CreateContext(HttpMethods.Put, ExemptPath)).WaitAsync(TestTimeout);
        Task held = middleware.Invoke(CreateContext(HttpMethods.Get, "/api/function"));

        Assert.Equal(1, _nextCalls);
        Assert.False(held.IsCompleted);
        _specialization.SetResult();
        await held.WaitAsync(TestTimeout);
        Assert.Equal(2, _nextCalls);
        _standbyManager.Verify(manager => manager.SpecializeHostAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task Invoke_NoExemptionRegistered_HoldsRequestUntilSpecializationCompletes()
    {
        Task request = CreateMiddleware().Invoke(CreateContext(HttpMethods.Put, ExemptPath));

        Assert.False(request.IsCompleted);
        Assert.Equal(0, _nextCalls);
        _specialization.SetResult();
        await request.WaitAsync(TestTimeout);
        Assert.Equal(1, _nextCalls);
    }

    [Fact]
    public async Task Invoke_AfterSpecialization_StopsCheckingSpecialization()
    {
        _specialization.SetResult();
        PlaceholderSpecializationMiddleware middleware = CreateMiddleware(_exemption.Object);

        await middleware.Invoke(CreateContext(HttpMethods.Get, "/api/function"));
        await middleware.Invoke(CreateContext(HttpMethods.Put, ExemptPath));

        Assert.Equal(2, _nextCalls);
        _standbyManager.Verify(manager => manager.SpecializeHostAsync(), Times.Once);
        _exemption.Verify(exemption => exemption.IsMatch(It.IsAny<HttpRequest>()), Times.Never);
    }

    private PlaceholderSpecializationMiddleware CreateMiddleware(params ISpecializationWaitExemption[] exemptions)
        => new(
            _ =>
            {
                Interlocked.Increment(ref _nextCalls);
                return Task.CompletedTask;
            },
            _webHostEnvironment.Object,
            _standbyManager.Object,
            _environment,
            exemptions);

    private static DefaultHttpContext CreateContext(string method, string path)
    {
        DefaultHttpContext context = new();
        context.Request.Method = method;
        context.Request.Path = path;

        return context;
    }
}
