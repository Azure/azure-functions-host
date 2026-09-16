// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class RpcClientScriptHostStartupCoordinatorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task StartAsync_NoLink_StartsRootWithoutStartingScriptHost()
    {
        await using RpcClientStartupTestHost testHost = new();

        await testHost.Host.StartAsync().WaitAsync(TestTimeout);
        await testHost.WaitEntered.Task.WaitAsync(TestTimeout);

        Assert.True(testHost.Host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested);
        Assert.False(testHost.Coordinator.ActivationTask.IsCompleted);
        testHost.ScriptHost.Verify(host => host.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.DoesNotContain(testHost.Host.Services.GetServices<IHostedService>(), service => ReferenceEquals(service, testHost.ScriptHost.Object));

        await testHost.Host.StopAsync().WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAsync_FirstOrPreexistingLink_StartsExactlyOnce(bool preexistingLink)
    {
        await using RpcClientStartupTestHost testHost = new();
        if (preexistingLink)
        {
            testHost.CompleteLink();
        }

        await testHost.Coordinator.StartAsync(CancellationToken.None);
        testHost.CompleteLink();
        Task activation = testHost.Coordinator.ActivationTask;
        await activation.WaitAsync(TestTimeout);
        await testHost.Coordinator.StartAsync(CancellationToken.None);
        testHost.CompleteLink();

        Assert.Same(activation, testHost.Coordinator.ActivationTask);
        Assert.True(activation.IsCompletedSuccessfully);
        testHost.Registry.Verify(registry => registry.WaitForFirstInitializedAsync(It.IsAny<CancellationToken>()), Times.Once);
        testHost.ScriptHost.Verify(host => host.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_ConcurrentCallsShareOneActivation()
    {
        await using RpcClientStartupTestHost testHost = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        testHost.ScriptHost.Setup(host => host.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                entered.TrySetResult();
                return release.Task;
            });

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => testHost.Coordinator.StartAsync(CancellationToken.None))));
        testHost.CompleteLink();
        await entered.Task.WaitAsync(TestTimeout);
        Assert.False(testHost.Coordinator.ActivationTask.IsCompleted);
        release.SetResult();
        await testHost.Coordinator.ActivationTask.WaitAsync(TestTimeout);

        testHost.Registry.Verify(registry => registry.WaitForFirstInitializedAsync(It.IsAny<CancellationToken>()), Times.Once);
        testHost.ScriptHost.Verify(host => host.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_UnexpectedFailureIsObservableWithoutRepeatingTheCall()
    {
        await using RpcClientStartupTestHost testHost = new();
        InvalidOperationException failure = new("Activation failed.");
        testHost.ScriptHost.Setup(host => host.StartAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        testHost.CompleteLink();
        await testHost.Coordinator.StartAsync(CancellationToken.None);
        Task activation = testHost.Coordinator.ActivationTask;
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => activation.WaitAsync(TestTimeout)));
        await testHost.Coordinator.StartAsync(CancellationToken.None);
        testHost.CompleteLink();

        Assert.Same(activation, testHost.Coordinator.ActivationTask);
        Assert.False(testHost.Host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        testHost.ScriptHost.Verify(host => host.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        testHost.Registry.Verify(registry => registry.DisposeAsync(), Times.Never);
    }

    [Fact]
    public async Task StartAsync_ReturningDuringManagerRetry_DoesNotBecomeAnActivationFailure()
    {
        await using RpcClientStartupTestHost testHost = new();
        InvalidOperationException failure = new("The manager is retrying startup.");
        testHost.ScriptHostManager.SetupGet(manager => manager.State).Returns(ScriptHostState.Error);
        testHost.ScriptHostManager.SetupGet(manager => manager.LastError).Returns(failure);
        testHost.CompleteLink();
        await testHost.Coordinator.StartAsync(CancellationToken.None);

        await testHost.Coordinator.ActivationTask.WaitAsync(TestTimeout);
        await testHost.Coordinator.StartAsync(CancellationToken.None);

        Assert.True(testHost.Coordinator.ActivationTask.IsCompletedSuccessfully);
        Assert.Equal(ScriptHostState.Error, testHost.ScriptHostManager.Object.State);
        Assert.Same(failure, testHost.ScriptHostManager.Object.LastError);
        testHost.ScriptHost.Verify(host => host.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_CanceledCallerDoesNotCancelExistingActivation()
    {
        await using RpcClientStartupTestHost testHost = new();
        using CancellationTokenSource caller = new();
        await testHost.Coordinator.StartAsync(CancellationToken.None);
        await testHost.WaitEntered.Task.WaitAsync(TestTimeout);
        caller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => testHost.Coordinator.StartAsync(caller.Token));
        testHost.CompleteLink();
        await testHost.Coordinator.ActivationTask.WaitAsync(TestTimeout);

        testHost.ScriptHost.Verify(host => host.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_BeforeStart_IsIdempotentAndPreventsActivation()
    {
        await using RpcClientStartupTestHost testHost = new();

        await Task.WhenAll(testHost.Coordinator.StopAsync(CancellationToken.None), testHost.Coordinator.StopAsync(CancellationToken.None))
            .WaitAsync(TestTimeout);
        testHost.CompleteLink();

        Assert.Null(testHost.Coordinator.ActivationTask);
        await Assert.ThrowsAsync<InvalidOperationException>(() => testHost.Coordinator.StartAsync(CancellationToken.None));
        testHost.ScriptHost.Verify(host => host.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
        testHost.ScriptHost.Verify(host => host.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_BeforeLink_CancelsOnlyTheCoordinatorWait()
    {
        await using RpcClientStartupTestHost testHost = new();
        await testHost.Coordinator.StartAsync(CancellationToken.None);
        await testHost.WaitEntered.Task.WaitAsync(TestTimeout);

        await testHost.Coordinator.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        testHost.CompleteLink();

        Assert.True(testHost.Coordinator.ActivationTask.IsCanceled);
        testHost.ScriptHost.Verify(host => host.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
        testHost.Registry.Verify(registry => registry.DisposeAsync(), Times.Never);
    }

    [Fact]
    public async Task StopAsync_DuringActivation_CancelsAndStopsTheServiceOnce()
    {
        await using RpcClientStartupTestHost testHost = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        testHost.ScriptHost.Setup(host => host.StartAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) =>
            {
                entered.SetResult();
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
        testHost.CompleteLink();
        await testHost.Coordinator.StartAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TestTimeout);

        await Task.WhenAll(testHost.Coordinator.StopAsync(CancellationToken.None), testHost.Coordinator.StopAsync(CancellationToken.None))
            .WaitAsync(TestTimeout);

        Assert.True(testHost.Coordinator.ActivationTask.IsCanceled);
        testHost.ScriptHost.Verify(host => host.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_CanceledBeforeEntry_DoesNotBeginShutdown()
    {
        await using RpcClientStartupTestHost testHost = new();
        using CancellationTokenSource caller = new();
        testHost.CompleteLink();
        await testHost.Coordinator.StartAsync(CancellationToken.None);
        await testHost.Coordinator.ActivationTask.WaitAsync(TestTimeout);
        caller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => testHost.Coordinator.StopAsync(caller.Token));

        testHost.ScriptHost.Verify(host => host.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StopAsync_CanceledCallerDoesNotAbandonOwnedCleanup()
    {
        await using RpcClientStartupTestHost testHost = new();
        using CancellationTokenSource caller = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        testHost.ScriptHost.Setup(host => host.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                entered.SetResult();
                return release.Task;
            });
        testHost.CompleteLink();
        await testHost.Coordinator.StartAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TestTimeout);

        Task canceledWait = testHost.Coordinator.StopAsync(caller.Token);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait.WaitAsync(TestTimeout));
        Task stop = testHost.Coordinator.StopAsync(CancellationToken.None);
        Assert.False(stop.IsCompleted);
        release.SetResult();
        await stop.WaitAsync(TestTimeout);

        testHost.ScriptHost.Verify(host => host.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotentAndWaitsForShutdown()
    {
        await using RpcClientStartupTestHost testHost = new();
        testHost.CompleteLink();
        await testHost.Coordinator.StartAsync(CancellationToken.None);
        await testHost.Coordinator.ActivationTask.WaitAsync(TestTimeout);
        TaskCompletionSource stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        testHost.ScriptHost.Setup(host => host.StopAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            stopping.SetResult();
            return stopped.Task;
        });

        Task firstDispose = testHost.Coordinator.DisposeAsync().AsTask();
        Task secondDispose = testHost.Coordinator.DisposeAsync().AsTask();
        Assert.Same(firstDispose, secondDispose);
        await stopping.Task.WaitAsync(TestTimeout);
        Assert.False(firstDispose.IsCompleted);
        stopped.SetResult();
        await firstDispose.WaitAsync(TestTimeout);

        testHost.ScriptHost.Verify(host => host.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        testHost.Registry.Verify(registry => registry.DisposeAsync(), Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_StopFailure_DoesNotPreventRootRegistryDisposal()
    {
        await AssertRootDisposalAfterStopFailureAsync(new InvalidOperationException("ScriptHost stop failed."), stopBeforeDispose: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeAsync_StopFailure_RemainsObservableAfterDisposal(bool canceled)
    {
        Exception failure = canceled ? new OperationCanceledException("ScriptHost stop timed out.") : new InvalidOperationException("ScriptHost stop failed.");

        await AssertRootDisposalAfterStopFailureAsync(failure, stopBeforeDispose: true);
    }

    [Fact]
    public async Task StopAsync_PreservesActivationAndShutdownFailures()
    {
        await using RpcClientStartupTestHost testHost = new();
        InvalidOperationException activationFailure = new("Activation failed.");
        InvalidOperationException shutdownFailure = new("Shutdown failed.");
        testHost.ScriptHost.Setup(host => host.StartAsync(It.IsAny<CancellationToken>())).ThrowsAsync(activationFailure);
        testHost.ScriptHost.Setup(host => host.StopAsync(It.IsAny<CancellationToken>())).ThrowsAsync(shutdownFailure);
        testHost.CompleteLink();
        await testHost.Coordinator.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => testHost.Coordinator.ActivationTask.WaitAsync(TestTimeout));

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() => testHost.Coordinator.StopAsync(CancellationToken.None));

        Assert.Equal([activationFailure, shutdownFailure], failure.InnerExceptions);
    }

    [Fact]
    public async Task ApplicationStopping_CancelsDeferredActivationBeforeStopAsync()
    {
        await using RpcClientStartupTestHost testHost = new();
        await testHost.Coordinator.StartAsync(CancellationToken.None);
        await testHost.WaitEntered.Task.WaitAsync(TestTimeout);

        testHost.Host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => testHost.Coordinator.ActivationTask.WaitAsync(TestTimeout));
        testHost.CompleteLink();

        testHost.ScriptHost.Verify(host => host.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task AssertRootDisposalAfterStopFailureAsync(Exception failure, bool stopBeforeDispose)
    {
        await using RpcClientStartupTestHost testHost = new();
        testHost.ScriptHost.Setup(host => host.StopAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        RpcClientScriptHostStartupCoordinator coordinator = testHost.Coordinator;
        testHost.CompleteLink();
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.ActivationTask.WaitAsync(TestTimeout);

        if (stopBeforeDispose)
        {
            Assert.Same(failure, await Assert.ThrowsAnyAsync<Exception>(() => coordinator.StopAsync(CancellationToken.None).WaitAsync(TestTimeout)));
        }

        await testHost.DisposeAsync();

        testHost.Registry.Verify(registry => registry.DisposeAsync(), Times.Once);
        testHost.ScriptHost.Verify(host => host.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Same(failure, await Assert.ThrowsAnyAsync<Exception>(() => coordinator.StopAsync(CancellationToken.None)));
        Assert.Contains(testHost.Logger.Invocations, invocation =>
            string.Equals(invocation.Method.Name, nameof(ILogger.Log), StringComparison.Ordinal) &&
            invocation.Arguments[0] is LogLevel.Error && invocation.Arguments[1] is EventId { Id: 5 } &&
            ReferenceEquals(invocation.Arguments[3], failure));
    }
}
