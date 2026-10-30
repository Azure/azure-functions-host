// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    [Theory]
    [InlineData(null, null, null, 75)]
    [InlineData("00:02:30", "00:00:20", "00:00:45", 175)]
    public async Task LinkBudgetAndChannelTimeouts_UseTheSameConfiguredWorkerOptions(
        string startup, string initialization, string reload, int totalSeconds)
    {
        ServiceCollection services = new();
        Dictionary<string, string> values = new();
        if (startup is not null)
        {
            values["RpcClient:Worker:ProcessStartupTimeout"] = startup;
            values["RpcClient:Worker:InitializationTimeout"] = initialization;
            values["RpcClient:Worker:EnvironmentReloadTimeout"] = reload;
        }
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddRpcClientServices();
        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<WorkerProcessCountOptions> options = provider.GetRequiredService<IOptionsMonitor<WorkerProcessCountOptions>>();
        WorkerProcessCountOptions expected = options.Get(RpcClientWorkerChannelFactory.WorkerOptionsName);
        await using WorkerChannelRegistry registry = new(Mock.Of<IDuplexChannelFactory<StreamingMessage>>(),
            Mock.Of<IRpcClientWorkerChannelFactory>(), NullLogger<WorkerChannelRegistry>.Instance,
            RuntimeWorkerTestData.CreateConfiguration(), TimeProvider.System, options);
        await using RpcClientWorkerChannel channel = CreateRealChannelFactory(processOptions: options)
            .Create("worker", new TestDuplexChannel<StreamingMessage>(), RuntimeWorkerTestData.CreateCredentials("worker"));

        Assert.Equal(TimeSpan.FromSeconds(totalSeconds), registry.LinkTimeout);
        Assert.Equal(expected.ProcessStartupTimeout, channel.WorkerConfig.CountOptions.ProcessStartupTimeout);
        Assert.Equal(expected.InitializationTimeout, channel.WorkerConfig.CountOptions.InitializationTimeout);
        Assert.Equal(expected.EnvironmentReloadTimeout, channel.WorkerConfig.CountOptions.EnvironmentReloadTimeout);
        Assert.Equal(RpcClientFactory.DefaultConnectTimeout + channel.WorkerConfig.CountOptions.ProcessStartupTimeout
            + channel.WorkerConfig.CountOptions.InitializationTimeout, registry.LinkTimeout);
    }

    [Fact]
    public async Task LinkDeadline_UsesTimeProviderAndPermitsRetryAfterDeterministicTimeout()
    {
        Mock<TimeProvider> time = new();
        Mock<ITimer> timer = new();
        TaskCompletionSource<Action> expire = new(TaskCreationOptions.RunContinuationsAsynchronously);
        time.Setup(clock => clock.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object>(),
            It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimerCallback callback, object state, TimeSpan due, TimeSpan period) =>
            {
                Assert.Equal(TimeSpan.FromSeconds(75), due);
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                expire.TrySetResult(() => callback(state));
                return timer.Object;
            });
        Mock<IDuplexChannelFactory<StreamingMessage>> transport = new();
        transport.Setup(factory => factory.ConnectAsync(It.IsAny<Uri>(), It.IsAny<RuntimeWorkerCredentials>(), It.IsAny<CancellationToken>()))
            .Returns((Uri _, RuntimeWorkerCredentials _, CancellationToken token) =>
                new TaskCompletionSource<DuplexChannel<StreamingMessage>>().Task.WaitAsync(token));
        await using WorkerChannelRegistry registry = new(transport.Object, Mock.Of<IRpcClientWorkerChannelFactory>(),
            NullLogger<WorkerChannelRegistry>.Instance, RuntimeWorkerTestData.CreateConfiguration(), time.Object);

        Task<WorkerLinkResult> first = registry.LinkAsync("worker", CreateEndpoint("worker"));
        (await expire.Task.WaitAsync(TestTimeout))();
        await Assert.ThrowsAsync<TimeoutException>(() => first.WaitAsync(TestTimeout));
        Assert.Empty(registry.GetInitializedChannels());

        using CancellationTokenSource cancellation = new();
        Task<WorkerLinkResult> retry = registry.LinkAsync("worker", CreateEndpoint("worker"), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry.WaitAsync(TestTimeout));
        transport.Verify(factory => factory.ConnectAsync(It.IsAny<Uri>(), It.IsAny<RuntimeWorkerCredentials>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("ProcessStartupTimeout", "00:00:00")]
    [InlineData("InitializationTimeout", "-00:00:01")]
    [InlineData("EnvironmentReloadTimeout", "100.00:00:00")]
    public void WorkerOptions_RejectUnboundedOrNonpositiveTimeouts(string setting, string value)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            [$"RpcClient:Worker:{setting}"] = value
        }).Build());
        services.AddRpcClientServices();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptionsMonitor<WorkerProcessCountOptions>>().Get(RpcClientWorkerChannelFactory.WorkerOptionsName));
    }
}
