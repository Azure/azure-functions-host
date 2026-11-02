// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    [Theory]
    [MemberData(nameof(WorkerHostConfigurationParserTests.InvalidDeliveries), MemberType = typeof(WorkerHostConfigurationParserTests))]
    public async Task LinkAsync_InvalidHostConfigurationDoesNotPublishOrPoisonFirstConfiguration(
        HostConfiguration delivery, string expectedReason)
    {
        RegistryHarness harness = new();
        KeyValuePair<string, object>[] loggedState = null;
        string message = null;
        harness.HostConfigurationLogger.Setup(logger => logger.Log(
            It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()))
            .Callback(new InvocationAction(call =>
            {
                // The generated logger reuses its state after Log returns.
                loggedState = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object>>>(call.Arguments[2]).ToArray();
                Delegate formatter = Assert.IsAssignableFrom<Delegate>(call.Arguments[4]);
                message = Assert.IsType<string>(formatter.DynamicInvoke(call.Arguments[2], call.Arguments[3]));
            }));
        ChannelControl rejected = new("worker") { HostConfiguration = delivery };
        ChannelControl replacement = new("worker") { HostConfiguration = CreateHostConfiguration("replacement") };
        harness.Enqueue(rejected);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<Microsoft.Azure.WebJobs.Script.Grpc.WorkerChannel> waiter = registry.WaitForFirstInitializedAsync();

        WorkerLinkException exception = await Assert.ThrowsAsync<WorkerLinkException>(() =>
            registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout));

        Assert.Equal(WorkerLinkFailureReason.Unavailable, exception.Reason);
        Assert.Equal(expectedReason, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.False(waiter.IsCompleted);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Equal(1, rejected.DisposeCount);
        await registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout);
        Assert.Same(replacement.Channel, await waiter.WaitAsync(TestTimeout));
        Assert.False(replacement.Channel.IsChannelReadyForInvocations());
        Assert.Equal("replacement", ReadHostConfiguration(registry)["AzureFunctionsJobHost:marker"]);
        Assert.Equal("1", replacement.InitRequest.Capabilities["HostConfiguration"]);

        var diagnostic = Assert.Single(harness.HostConfigurationLogger.Invocations, invocation =>
            string.Equals(invocation.Method.Name, nameof(ILogger.Log), StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, diagnostic.Arguments[0]);
        Assert.Equal(new EventId(0, "HostConfigurationRejected"), diagnostic.Arguments[1]);
        Assert.Null(diagnostic.Arguments[3]);
        Assert.Equal($"Host configuration was rejected. {expectedReason}", message);
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
                Assert.Equal("Host configuration was rejected. {Reason}", format.Value);
            });
    }

    [Fact]
    public async Task LinkAsync_AbsentHostConfigurationSelectsFileBasedLoadingOnlyAfterAdmission()
    {
        RegistryHarness harness = new();
        ChannelControl worker = new("worker", blockStart: true);
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<Microsoft.Azure.WebJobs.Script.Grpc.WorkerChannel> waiter = registry.WaitForFirstInitializedAsync();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Equal("Host configuration source has not been selected.", exception.Message);
        Task<WorkerLinkResult> link = registry.LinkAsync("worker", CreateEndpoint("worker"));
        await worker.StartEntered.WaitAsync(TestTimeout);
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.False(waiter.IsCompleted);
        worker.AllowStart();
        await link.WaitAsync(TestTimeout);

        Assert.Equal("1", worker.InitRequest.Capabilities["HostConfiguration"]);
        Assert.Null(worker.Channel.HostConfiguration);
        Assert.Null(registry.GetConfigurationSource());
        Assert.Same(worker.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Same(worker.Channel, await waiter.WaitAsync(TestTimeout));
        harness.HostConfigurationLogger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAsync_RetainsExactSourceReturnedByParser()
    {
        var parser = new Mock<IWorkerHostConfigurationParser>(MockBehavior.Strict);
        var firstSource = new Mock<IConfigurationSource>(MockBehavior.Strict);
        var laterSource = new Mock<IConfigurationSource>(MockBehavior.Strict);
        ChannelControl first = new("first") { HostConfiguration = CreateHostConfiguration("first") };
        ChannelControl later = new("later") { HostConfiguration = CreateHostConfiguration("later") };
        parser.Setup(value => value.Parse(first.HostConfiguration)).Returns(firstSource.Object);
        parser.Setup(value => value.Parse(later.HostConfiguration)).Returns(laterSource.Object);
        RegistryHarness harness = new(null, workerHostConfigurationParser: parser.Object);
        harness.Enqueue(first);
        harness.Enqueue(later);
        await using WorkerChannelRegistry registry = harness.Registry;
        IHostConfigurationSourceProvider sourceProvider = registry;

        await registry.LinkAsync("first", CreateEndpoint("first")).WaitAsync(TestTimeout);

        Assert.Same(firstSource.Object, sourceProvider.GetConfigurationSource());

        await registry.LinkAsync("later", CreateEndpoint("later")).WaitAsync(TestTimeout);

        Assert.Same(firstSource.Object, sourceProvider.GetConfigurationSource());
        Assert.True(await registry.UnlinkAsync("first"));
        Assert.True(await registry.UnlinkAsync("later"));
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Same(firstSource.Object, sourceProvider.GetConfigurationSource());
        parser.Verify(value => value.Parse(first.HostConfiguration), Times.Once);
        parser.Verify(value => value.Parse(later.HostConfiguration), Times.Once);
        parser.VerifyNoOtherCalls();
        firstSource.VerifyNoOtherCalls();
        laterSource.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAsync_ParserConfigurationFailureUsesExistingLinkFailureContract()
    {
        var parser = new Mock<IWorkerHostConfigurationParser>(MockBehavior.Strict);
        ChannelControl worker = new("worker") { HostConfiguration = CreateHostConfiguration("rejected") };
        parser.Setup(value => value.Parse(worker.HostConfiguration))
            .Throws(new HostConfigurationException("The supplied Host configuration was rejected."));
        RegistryHarness harness = new(null, workerHostConfigurationParser: parser.Object);
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<Microsoft.Azure.WebJobs.Script.Grpc.WorkerChannel> waiter = registry.WaitForFirstInitializedAsync();

        WorkerLinkException exception = await Assert.ThrowsAsync<WorkerLinkException>(() =>
            registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout));

        Assert.Equal(WorkerLinkFailureReason.Unavailable, exception.Reason);
        Assert.Equal("The supplied Host configuration was rejected.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.False(waiter.IsCompleted);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Equal(1, worker.DisposeCount);
        parser.Verify(value => value.Parse(worker.HostConfiguration), Times.Once);
        parser.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAsync_UnexpectedParserFailureIsNotTranslated()
    {
        var parser = new Mock<IWorkerHostConfigurationParser>(MockBehavior.Strict);
        ChannelControl worker = new("worker") { HostConfiguration = CreateHostConfiguration("rejected") };
        InvalidOperationException failure = new("Unexpected parser failure.");
        parser.Setup(value => value.Parse(worker.HostConfiguration)).Throws(failure);
        RegistryHarness harness = new(null, workerHostConfigurationParser: parser.Object);
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout));

        Assert.Same(failure, exception);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Equal(1, worker.DisposeCount);
        parser.Verify(value => value.Parse(worker.HostConfiguration), Times.Once);
        parser.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LinkAsync_MixedDeliveriesAndWorkerLossRetainFirstSource(
        bool firstSuppliesConfiguration, bool registryOwnedAttempt)
    {
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), registryOwnedAttempt: registryOwnedAttempt);
        ChannelControl first = new("first") { HostConfiguration = firstSuppliesConfiguration ? CreateHostConfiguration("first") : null };
        ChannelControl later = new("later") { HostConfiguration = firstSuppliesConfiguration ? null : CreateHostConfiguration("later") };
        harness.Enqueue(first);
        harness.Enqueue(later);
        await using WorkerChannelRegistry registry = harness.Registry;

        await registry.LinkAsync("first", CreateEndpoint("first")).WaitAsync(TestTimeout);
        IConfigurationSource source = registry.GetConfigurationSource();
        if (firstSuppliesConfiguration)
        {
            Assert.Equal("first", ReadHostConfiguration(registry)["AzureFunctionsJobHost:marker"]);
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
        harness.HostConfigurationLogger.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkAsync_RejectedCandidateDoesNotPreventFileBasedLoadingSelection(bool registryOwnedAttempt)
    {
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), registryOwnedAttempt: registryOwnedAttempt);
        ChannelControl rejected = new("worker") { HostConfiguration = new() { Status = HostConfiguration.Types.Status.Error } };
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
    [InlineData(false, HostConfiguration.Types.Status.Error)]
    [InlineData(true, HostConfiguration.Types.Status.Error)]
    [InlineData(false, HostConfiguration.Types.Status.TooLarge)]
    [InlineData(true, HostConfiguration.Types.Status.TooLarge)]
    [InlineData(false, (HostConfiguration.Types.Status)5)]
    [InlineData(true, (HostConfiguration.Types.Status)5)]
    public async Task LinkAsync_LaterInvalidDeliveryRejectsWithoutReplacingSource(
        bool firstSuppliesConfiguration, HostConfiguration.Types.Status invalidStatus)
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first") { HostConfiguration = firstSuppliesConfiguration ? CreateHostConfiguration("first") : null };
        ChannelControl rejected = new("later") { HostConfiguration = new() { Status = invalidStatus } };
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
        Assert.Single(harness.HostConfigurationLogger.Invocations, invocation =>
            string.Equals(invocation.Method.Name, nameof(ILogger.Log), StringComparison.Ordinal));
    }

    [Fact]
    public async Task LinkAsync_HostConfigurationRequiresSuccessfulInitializationStatus()
    {
        RegistryHarness harness = new();
        ChannelControl worker = new("worker")
        {
            HostConfiguration = CreateHostConfiguration("not-successful"),
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
        ChannelControl first = new("first") { HostConfiguration = CreateHostConfiguration("first") };
        ChannelControl later = new("later") { HostConfiguration = CreateHostConfiguration("different") };
        harness.Enqueue(first);
        harness.Enqueue(later);
        await using WorkerChannelRegistry registry = harness.Registry;

        await registry.LinkAsync("first", CreateEndpoint("first")).WaitAsync(TestTimeout);
        IConfigurationSource snapshot = registry.GetConfigurationSource();
        first.HostConfiguration.Content = ByteString.CopyFromUtf8("mutated response");
        Assert.True(await registry.UnlinkAsync("first"));
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Same(snapshot, registry.GetConfigurationSource());
        await registry.LinkAsync("later", CreateEndpoint("later")).WaitAsync(TestTimeout);

        Assert.Same(snapshot, registry.GetConfigurationSource());
        Assert.Equal("first", ReadHostConfiguration(registry)["AzureFunctionsJobHost:marker"]);
        Assert.Same(later.Channel, Assert.Single(registry.GetInitializedChannels()));
        harness.HostConfigurationLogger.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkAsync_CanceledHandshakeDoesNotSelectSnapshotOrFileBasedLoading(bool suppliesConfiguration)
    {
        using CancellationTokenSource cancellation = new();
        RegistryHarness harness = new();
        ChannelControl canceled = new("worker", blockStart: true)
        {
            HostConfiguration = suppliesConfiguration ? CreateHostConfiguration("canceled") : null,
        };
        ChannelControl replacement = new("worker")
        {
            HostConfiguration = suppliesConfiguration ? null : CreateHostConfiguration("replacement"),
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
            Assert.Equal("replacement", ReadHostConfiguration(registry)["AzureFunctionsJobHost:marker"]);
        }

        harness.HostConfigurationLogger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAsync_CanceledDuringValidationDoesNotCommitConfiguration()
    {
        using CancellationTokenSource cancellation = new();
        var configuration = new Mock<IConfiguration>();
        configuration.SetupGet(value => value["AzureFunctionsJobHost:configurationProfile"])
            .Callback(() => cancellation.Cancel()).Returns((string)null);
        RegistryHarness harness = new(null, configuration.Object);
        ChannelControl canceled = new("worker") { HostConfiguration = CreateHostConfiguration("canceled") };
        ChannelControl replacement = new("replacement") { HostConfiguration = CreateHostConfiguration("replacement") };
        harness.Enqueue(canceled);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            registry.LinkAsync("worker", CreateEndpoint("worker"), cancellation.Token).WaitAsync(TestTimeout));

        Assert.Empty(registry.GetInitializedChannels());
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Equal(1, canceled.DisposeCount);
        await registry.LinkAsync("replacement", CreateEndpoint("replacement")).WaitAsync(TestTimeout);
        Assert.Equal("replacement", ReadHostConfiguration(registry)["AzureFunctionsJobHost:marker"]);
        harness.HostConfigurationLogger.VerifyNoOtherCalls();
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
        ChannelControl worker = new("worker") { HostConfiguration = CreateHostConfiguration("disposed") };
        harness.Enqueue(worker);

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout));
        await disposal.WaitAsync(TestTimeout);

        Assert.Empty(registry.GetInitializedChannels());
        Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
        Assert.Equal(1, worker.DisposeCount);
        harness.HostConfigurationLogger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAsync_ClosedDuringValidationDoesNotCommitConfiguration()
    {
        ChannelControl worker = new("worker") { HostConfiguration = CreateHostConfiguration("closed") };
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
        harness.HostConfigurationLogger.VerifyNoOtherCalls();
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
        ChannelControl slow = new("slow") { HostConfiguration = CreateHostConfiguration("slow") };
        ChannelControl winner = new("winner") { HostConfiguration = winnerSuppliesConfiguration ? CreateHostConfiguration("winner") : null };
        harness.Enqueue(slow);
        harness.Enqueue(winner);
        await using WorkerChannelRegistry registry = harness.Registry;
        // Blocking validation must not occupy the test's synchronization context.
        Task<WorkerLinkResult> slowLink = Task.Run(() => registry.LinkAsync("slow", CreateEndpoint("slow")));
        IConfigurationSource source = null;

        try
        {
            await entered.Task.WaitAsync(TestTimeout);
            Assert.Throws<InvalidOperationException>(() => registry.GetConfigurationSource());
            WorkerLinkResult accepted = await registry.LinkAsync("winner", CreateEndpoint("winner")).WaitAsync(TestTimeout);
            Assert.Same(winner.Channel, accepted.Channel);
            Assert.False(slowLink.IsCompleted);
            source = registry.GetConfigurationSource();
            if (winnerSuppliesConfiguration)
            {
                Assert.Equal("winner", ReadHostConfiguration(registry)["AzureFunctionsJobHost:marker"]);
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

    private static HostConfiguration CreateHostConfiguration(string marker) => new()
    {
        Status = HostConfiguration.Types.Status.Available,
        Content = ByteString.CopyFromUtf8($$"""{"version":"2.0","marker":"{{marker}}"}"""),
    };

    private static IConfigurationRoot ReadHostConfiguration(WorkerChannelRegistry registry)
        => new ConfigurationBuilder().Add(registry.GetConfigurationSource()).Build();
}
