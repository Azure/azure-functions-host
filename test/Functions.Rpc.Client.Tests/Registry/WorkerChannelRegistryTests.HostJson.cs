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
        RegistryHarness harness = new(null, enableWorkerHostJson: true);
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
        Assert.Equal("1", replacement.InitRequest.Capabilities["HostJson"]);

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
    public async Task LinkAsync_DisabledHostJsonIgnoresUnrequestedDeliveryAndUsesFileSource()
    {
        RegistryHarness harness = new();
        ChannelControl worker = new("worker")
        {
            HostJson = new() { Status = HostJsonConfiguration.Types.Status.ReadError, Content = ByteString.CopyFromUtf8("ignored") },
        };
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;

        await registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout);

        Assert.False(worker.InitRequest.Capabilities.ContainsKey("HostJson"));
        Assert.Null(worker.Channel.HostJson);
        Assert.Null(registry.GetConfigurationSource());
        Assert.Same(worker.Channel, Assert.Single(registry.GetInitializedChannels()));
        harness.HostJsonLogger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAsync_HostJsonRequiresSuccessfulInitializationStatus()
    {
        RegistryHarness harness = new(null, enableWorkerHostJson: true);
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
        RegistryHarness harness = new(null, enableWorkerHostJson: true);
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

    [Fact]
    public async Task LinkAsync_CanceledDuringValidationDoesNotCommitConfiguration()
    {
        using CancellationTokenSource cancellation = new();
        var configuration = new Mock<IConfiguration>();
        configuration.SetupGet(value => value["AzureFunctionsJobHost:configurationProfile"])
            .Callback(() => cancellation.Cancel()).Returns((string)null);
        RegistryHarness harness = new(null, enableWorkerHostJson: true, configuration.Object);
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
        RegistryHarness harness = new(null, enableWorkerHostJson: true, configuration.Object);
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
        RegistryHarness harness = new(null, enableWorkerHostJson: true, configuration.Object);
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;

        await Assert.ThrowsAsync<System.IO.IOException>(() =>
            registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout));

        Assert.Empty(registry.GetInitializedChannels());
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Equal(1, worker.DisposeCount);
        harness.HostJsonLogger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAsync_ConcurrentValidationDoesNotBlockAdmissionAndFirstPublishedSnapshotWins()
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
        RegistryHarness harness = new(null, enableWorkerHostJson: true, configuration.Object);
        ChannelControl slow = new("slow") { HostJson = CreateHostJson("slow") };
        ChannelControl winner = new("winner") { HostJson = CreateHostJson("winner") };
        harness.Enqueue(slow);
        harness.Enqueue(winner);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<WorkerLinkResult> slowLink = registry.LinkAsync("slow", CreateEndpoint("slow"));
        await entered.Task.WaitAsync(TestTimeout);

        try
        {
            WorkerLinkResult accepted = await registry.LinkAsync("winner", CreateEndpoint("winner")).WaitAsync(TestTimeout);
            Assert.Same(winner.Channel, accepted.Channel);
            Assert.False(slowLink.IsCompleted);
            Assert.Equal("winner", ReadHostJson(registry)["AzureFunctionsJobHost:marker"]);
        }
        finally
        {
            release.Set();
        }

        await slowLink.WaitAsync(TestTimeout);
        Assert.Equal(2, registry.GetInitializedChannels().Count);
        Assert.Equal("winner", ReadHostJson(registry)["AzureFunctionsJobHost:marker"]);
    }

    private static HostJsonConfiguration CreateHostJson(string marker) => new()
    {
        Status = HostJsonConfiguration.Types.Status.Available,
        Content = ByteString.CopyFromUtf8($$"""{"version":"2.0","marker":"{{marker}}"}"""),
    };

    private static IConfigurationRoot ReadHostJson(WorkerChannelRegistry registry)
        => new ConfigurationBuilder().Add(registry.GetConfigurationSource()).Build();
}
