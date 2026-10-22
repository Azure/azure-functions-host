// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    [Theory]
    [MemberData(nameof(WorkerHostJsonConfigurationTests.InvalidDeliveries), MemberType = typeof(WorkerHostJsonConfigurationTests))]
    public async Task LinkAsync_InvalidHostJsonDoesNotPublishOrPoisonFirstConfiguration(
        HostJsonConfiguration delivery, string expectedReason)
    {
        RegistryHarness harness = new();
        KeyValuePair<string, object>[] loggedState = null;
        string message = null;
        harness.HostJsonLogger.Setup(logger => logger.Log(
            It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()))
            .Callback(new InvocationAction(call =>
            {
                // The generated logger reuses its state after Log returns.
                loggedState = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object>>>(call.Arguments[2]).ToArray();
                Delegate formatter = Assert.IsAssignableFrom<Delegate>(call.Arguments[4]);
                message = Assert.IsType<string>(formatter.DynamicInvoke(call.Arguments[2], call.Arguments[3]));
            }));
        ChannelControl rejected = new("worker") { HostJson = delivery };
        ChannelControl replacement = new("worker") { HostJson = CreateHostJson("replacement") };
        harness.Enqueue(rejected);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<Microsoft.Azure.WebJobs.Script.Grpc.WorkerChannel> waiter = registry.WaitForFirstInitializedAsync();

        WorkerLinkException exception = await Assert.ThrowsAsync<WorkerLinkException>(() =>
            registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout));

        Assert.Equal(WorkerLinkFailureReason.Unavailable, exception.Reason);
        Assert.Equal(expectedReason, exception.Message);
        Assert.False(waiter.IsCompleted);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Equal(1, rejected.DisposeCount);
        await registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout);
        Assert.Same(replacement.Channel, await waiter.WaitAsync(TestTimeout));
        Assert.False(replacement.Channel.IsChannelReadyForInvocations());
        Assert.Equal("replacement", ReadHostJson(registry)["AzureFunctionsJobHost:marker"]);
        Assert.False(replacement.InitRequest.Capabilities.ContainsKey("HostJson"));

        var diagnostic = Assert.Single(harness.HostJsonLogger.Invocations, invocation =>
            string.Equals(invocation.Method.Name, nameof(ILogger.Log), StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, diagnostic.Arguments[0]);
        Assert.Equal(new EventId(0, "HostJsonConfigurationRejected"), diagnostic.Arguments[1]);
        Assert.Null(diagnostic.Arguments[3]);
        Assert.Equal($"Worker host.json configuration was rejected. {expectedReason}", message);
        Assert.InRange(message.Length, 1, 240);
        Assert.Collection(loggedState,
            reason =>
            {
                Assert.Equal("Reason", reason.Key);
                Assert.Equal(expectedReason, reason.Value);
            },
            format =>
            {
                Assert.Equal("{OriginalFormat}", format.Key);
                Assert.Equal("Worker host.json configuration was rejected. {Reason}", format.Value);
            });
    }

    [Fact]
    public async Task LinkAsync_AbsentHostJsonSelectsLegacyOnlyAfterAdmission()
    {
        RegistryHarness harness = new();
        ChannelControl worker = new("worker", blockStart: true);
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<Microsoft.Azure.WebJobs.Script.Grpc.WorkerChannel> waiter = registry.WaitForFirstInitializedAsync();

        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Task<WorkerLinkResult> link = registry.LinkAsync("worker", CreateEndpoint("worker"));
        await worker.StartEntered.WaitAsync(TestTimeout);
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.False(waiter.IsCompleted);
        worker.AllowStart();
        await link.WaitAsync(TestTimeout);

        Assert.False(worker.InitRequest.Capabilities.ContainsKey("HostJson"));
        Assert.Null(worker.Channel.HostJson);
        Assert.Null(registry.GetConfigurationSource());
        Assert.Same(worker.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Same(worker.Channel, await waiter.WaitAsync(TestTimeout));
        harness.HostJsonLogger.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkAsync_MixedDeliveriesAndWorkerLossRetainFirstSource(bool firstSuppliesConfiguration)
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first") { HostJson = firstSuppliesConfiguration ? CreateHostJson("first") : null };
        ChannelControl later = new("later") { HostJson = firstSuppliesConfiguration ? null : CreateHostJson("later") };
        harness.Enqueue(first);
        harness.Enqueue(later);
        await using WorkerChannelRegistry registry = harness.Registry;

        await registry.LinkAsync("first", CreateEndpoint("first")).WaitAsync(TestTimeout);
        IConfigurationSource source = registry.GetConfigurationSource();
        if (firstSuppliesConfiguration)
        {
            Assert.Equal("first", ReadHostJson(registry)["AzureFunctionsJobHost:marker"]);
        }
        else
        {
            Assert.Null(source);
        }

        await registry.LinkAsync("later", CreateEndpoint("later")).WaitAsync(TestTimeout);

        Assert.Same(source, registry.GetConfigurationSource());
        Assert.Equal(2, registry.GetInitializedChannels().Count);
        Assert.True(await registry.UnlinkAsync("first"));
        Assert.True(await registry.UnlinkAsync("later"));
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Same(source, registry.GetConfigurationSource());
        harness.HostJsonLogger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAsync_RejectedCandidateDoesNotPreventLegacySelection()
    {
        RegistryHarness harness = new();
        ChannelControl rejected = new("worker") { HostJson = new() { Status = HostJsonConfiguration.Types.Status.ReadError } };
        ChannelControl replacement = new("worker");
        harness.Enqueue(rejected);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;

        await Assert.ThrowsAsync<WorkerLinkException>(() =>
            registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout));

        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Empty(registry.GetInitializedChannels());
        await registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout);
        Assert.Null(registry.GetConfigurationSource());
        Assert.Same(replacement.Channel, Assert.Single(registry.GetInitializedChannels()));
    }

    [Theory]
    [InlineData(false, HostJsonConfiguration.Types.Status.ReadError)]
    [InlineData(true, HostJsonConfiguration.Types.Status.ReadError)]
    [InlineData(false, HostJsonConfiguration.Types.Status.TooLarge)]
    [InlineData(true, HostJsonConfiguration.Types.Status.TooLarge)]
    [InlineData(false, (HostJsonConfiguration.Types.Status)5)]
    [InlineData(true, (HostJsonConfiguration.Types.Status)5)]
    public async Task LinkAsync_LaterInvalidDeliveryRejectsWithoutReplacingSource(
        bool firstSuppliesConfiguration, HostJsonConfiguration.Types.Status invalidStatus)
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first") { HostJson = firstSuppliesConfiguration ? CreateHostJson("first") : null };
        ChannelControl rejected = new("later") { HostJson = new() { Status = invalidStatus } };
        ChannelControl replacement = new("later");
        harness.Enqueue(first);
        harness.Enqueue(rejected);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        await registry.LinkAsync("first", CreateEndpoint("first")).WaitAsync(TestTimeout);
        IConfigurationSource source = registry.GetConfigurationSource();

        await Assert.ThrowsAsync<WorkerLinkException>(() =>
            registry.LinkAsync("later", CreateEndpoint("later")).WaitAsync(TestTimeout));

        Assert.Same(source, registry.GetConfigurationSource());
        Assert.Same(first.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Equal(1, rejected.DisposeCount);
        await registry.LinkAsync("later", CreateEndpoint("later")).WaitAsync(TestTimeout);
        Assert.Same(source, registry.GetConfigurationSource());
        Assert.Equal(2, registry.GetInitializedChannels().Count);
        Assert.Single(harness.HostJsonLogger.Invocations, invocation =>
            string.Equals(invocation.Method.Name, nameof(ILogger.Log), StringComparison.Ordinal));
    }

    [Fact]
    public async Task LinkAsync_HostJsonRequiresSuccessfulInitializationStatus()
    {
        RegistryHarness harness = new();
        ChannelControl worker = new("worker")
        {
            HostJson = CreateHostJson("not-successful"),
            InitResult = new() { Status = (StatusResult.Types.Status)100 },
        };
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;

        await Assert.ThrowsAsync<WorkerLinkException>(() =>
            registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout));

        Assert.Empty(registry.GetInitializedChannels());
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
    }

    [Fact]
    public async Task LinkAsync_LaterDifferentConfigurationAndWorkerLossRetainFirstSnapshot()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first") { HostJson = CreateHostJson("first") };
        ChannelControl later = new("later") { HostJson = CreateHostJson("different") };
        harness.Enqueue(first);
        harness.Enqueue(later);
        await using WorkerChannelRegistry registry = harness.Registry;

        await registry.LinkAsync("first", CreateEndpoint("first")).WaitAsync(TestTimeout);
        IConfigurationSource snapshot = registry.GetConfigurationSource();
        first.HostJson.Content = ByteString.CopyFromUtf8("mutated response");
        Assert.True(await registry.UnlinkAsync("first"));
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Same(snapshot, registry.GetConfigurationSource());
        await registry.LinkAsync("later", CreateEndpoint("later")).WaitAsync(TestTimeout);

        Assert.Same(snapshot, registry.GetConfigurationSource());
        Assert.Equal("first", ReadHostJson(registry)["AzureFunctionsJobHost:marker"]);
        Assert.Same(later.Channel, Assert.Single(registry.GetInitializedChannels()));
        harness.HostJsonLogger.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkAsync_CanceledHandshakeDoesNotSelectSnapshotOrLegacy(bool suppliesConfiguration)
    {
        using CancellationTokenSource cancellation = new();
        RegistryHarness harness = new();
        ChannelControl canceled = new("worker", blockStart: true)
        {
            HostJson = suppliesConfiguration ? CreateHostJson("canceled") : null,
        };
        ChannelControl replacement = new("worker")
        {
            HostJson = suppliesConfiguration ? null : CreateHostJson("replacement"),
        };
        harness.Enqueue(canceled);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<Microsoft.Azure.WebJobs.Script.Grpc.WorkerChannel> waiter = registry.WaitForFirstInitializedAsync();
        Task<WorkerLinkResult> link = registry.LinkAsync("worker", CreateEndpoint("worker"), cancellation.Token);
        await canceled.StartEntered.WaitAsync(TestTimeout);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => link.WaitAsync(TestTimeout));

        Assert.False(waiter.IsCompleted);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Equal(1, canceled.DisposeCount);
        await registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout);
        Assert.Same(replacement.Channel, await waiter.WaitAsync(TestTimeout));
        if (suppliesConfiguration)
        {
            Assert.Null(registry.GetConfigurationSource());
        }
        else
        {
            Assert.Equal("replacement", ReadHostJson(registry)["AzureFunctionsJobHost:marker"]);
        }

        harness.HostJsonLogger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAsync_CanceledDuringValidationDoesNotCommitConfiguration()
    {
        using CancellationTokenSource cancellation = new();
        var configuration = new Mock<IConfiguration>();
        configuration.SetupGet(value => value["AzureFunctionsJobHost:configurationProfile"])
            .Callback(() => cancellation.Cancel()).Returns((string)null);
        RegistryHarness harness = new(null, configuration.Object);
        ChannelControl canceled = new("worker") { HostJson = CreateHostJson("canceled") };
        ChannelControl replacement = new("replacement") { HostJson = CreateHostJson("replacement") };
        harness.Enqueue(canceled);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            registry.LinkAsync("worker", CreateEndpoint("worker"), cancellation.Token).WaitAsync(TestTimeout));

        Assert.Empty(registry.GetInitializedChannels());
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Equal(1, canceled.DisposeCount);
        await registry.LinkAsync("replacement", CreateEndpoint("replacement")).WaitAsync(TestTimeout);
        Assert.Equal("replacement", ReadHostJson(registry)["AzureFunctionsJobHost:marker"]);
        harness.HostJsonLogger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAsync_DisposedDuringValidationDoesNotCommitConfiguration()
    {
        Task disposal = Task.CompletedTask;
        var configuration = new Mock<IConfiguration>();
        RegistryHarness harness = new(null, configuration.Object);
        WorkerChannelRegistry registry = harness.Registry;
        configuration.SetupGet(value => value["AzureFunctionsJobHost:configurationProfile"])
            .Callback(() => disposal = registry.DisposeAsync().AsTask()).Returns((string)null);
        ChannelControl worker = new("worker") { HostJson = CreateHostJson("disposed") };
        harness.Enqueue(worker);

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout));
        await disposal.WaitAsync(TestTimeout);

        Assert.Empty(registry.GetInitializedChannels());
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Equal(1, worker.DisposeCount);
        harness.HostJsonLogger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAsync_ClosedDuringValidationDoesNotCommitConfiguration()
    {
        ChannelControl worker = new("worker") { HostJson = CreateHostJson("closed") };
        var configuration = new Mock<IConfiguration>();
        configuration.SetupGet(value => value["AzureFunctionsJobHost:configurationProfile"])
            .Callback(() => worker.Complete()).Returns((string)null);
        RegistryHarness harness = new(null, configuration.Object);
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;

        await Assert.ThrowsAsync<System.IO.IOException>(() =>
            registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout));

        Assert.Empty(registry.GetInitializedChannels());
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Equal(1, worker.DisposeCount);
        harness.HostJsonLogger.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkAsync_ConcurrentValidationDoesNotBlockAdmissionAndFirstSourceWins(bool winnerSuppliesConfiguration)
    {
        using ManualResetEventSlim release = new(initialState: false);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int validations = 0;
        var configuration = new Mock<IConfiguration>();
        configuration.SetupGet(value => value["AzureFunctionsJobHost:configurationProfile"])
            .Callback(() =>
            {
                if (Interlocked.Increment(ref validations) == 1)
                {
                    entered.TrySetResult();
                    Assert.True(release.Wait(TestTimeout));
                }
            })
            .Returns((string)null);
        RegistryHarness harness = new(null, configuration.Object);
        ChannelControl slow = new("slow") { HostJson = CreateHostJson("slow") };
        ChannelControl winner = new("winner") { HostJson = winnerSuppliesConfiguration ? CreateHostJson("winner") : null };
        harness.Enqueue(slow);
        harness.Enqueue(winner);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<WorkerLinkResult> slowLink = registry.LinkAsync("slow", CreateEndpoint("slow"));
        await entered.Task.WaitAsync(TestTimeout);
        IConfigurationSource source = null;

        try
        {
            Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
            WorkerLinkResult accepted = await registry.LinkAsync("winner", CreateEndpoint("winner")).WaitAsync(TestTimeout);
            Assert.Same(winner.Channel, accepted.Channel);
            Assert.False(slowLink.IsCompleted);
            source = registry.GetConfigurationSource();
            if (winnerSuppliesConfiguration)
            {
                Assert.Equal("winner", ReadHostJson(registry)["AzureFunctionsJobHost:marker"]);
            }
            else
            {
                Assert.Null(source);
            }
        }
        finally
        {
            release.Set();
        }

        await slowLink.WaitAsync(TestTimeout);
        Assert.Equal(2, registry.GetInitializedChannels().Count);
        Assert.Same(source, registry.GetConfigurationSource());
    }

    private static HostJsonConfiguration CreateHostJson(string marker) => new()
    {
        Status = HostJsonConfiguration.Types.Status.Available,
        Content = ByteString.CopyFromUtf8($$"""{"version":"2.0","marker":"{{marker}}"}"""),
    };

    private static IConfigurationRoot ReadHostJson(WorkerChannelRegistry registry)
        => new ConfigurationBuilder().Add(registry.GetConfigurationSource()).Build();
}
