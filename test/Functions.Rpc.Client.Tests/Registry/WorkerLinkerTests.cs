// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Moq;
using Xunit;
using GrpcException = Grpc.Core.RpcException;
using WorkerRpcException = Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class WorkerLinkerTests
{
    private const string WorkerId = "worker-pod-abc123";
    private const string PrivateDiagnostic = "private-worker-initialization-diagnostic";
    private static readonly Uri Endpoint = new("http://worker-proxy:50053");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private readonly Mock<IWorkerChannelRegistry> _registry = new(MockBehavior.Strict);

    [Fact]
    public void Constructor_NullRegistry_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkerLinker(null!));
        Assert.Throws<ArgumentNullException>(() => new WorkerLinker(null!, TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void Constructor_InvalidTimeout_Throws(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorkerLinker(_registry.Object, TimeSpan.FromMilliseconds(milliseconds)));
        _registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkAsync_AwaitsInitializationAndForwardsCancelableToken(bool callerCanCancel)
    {
        using CancellationTokenSource cancellation = new();
        CancellationToken callerToken = callerCanCancel ? cancellation.Token : CancellationToken.None;
        TaskCompletionSource<CancellationToken> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<WorkerChannel> initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(registry => registry.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .Callback<string, Uri, CancellationToken>((workerId, endpoint, token) =>
            {
                Assert.Equal(WorkerId, workerId);
                Assert.Same(Endpoint, endpoint);
                entered.SetResult(token);
            })
            .Returns(initialization.Task);
        IWorkerLinker linker = new WorkerLinker(_registry.Object);

        Task link = linker.LinkAsync(WorkerId, Endpoint, callerToken);
        CancellationToken registryToken = await entered.Task.WaitAsync(TestTimeout);

        Assert.True(registryToken.CanBeCanceled);
        Assert.NotEqual(callerToken, registryToken);
        Assert.False(registryToken.IsCancellationRequested);
        Assert.False(link.IsCompleted);
        Assert.False(link is Task<WorkerChannel>);
        initialization.SetResult(null!);
        await link.WaitAsync(TestTimeout);

        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkAsync_CompletedReplay_CompletesSynchronously()
    {
        _registry.Setup(registry => registry.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkerChannel)null!);
        WorkerLinker linker = new(_registry.Object);

        Task link = linker.LinkAsync(WorkerId, Endpoint);

        Assert.True(link.IsCompletedSuccessfully);
        await link;
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkAsync_SynchronousRejection_PreservesFailure()
    {
        WorkerLinkException failure = new(WorkerLinkFailureReason.Conflict, "The worker endpoint conflicts.");
        _registry.Setup(registry => registry.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .Throws(failure);
        WorkerLinker linker = new(_registry.Object);

        Task link = linker.LinkAsync(WorkerId, Endpoint);

        Assert.True(link.IsFaulted);
        WorkerLinkException actual = await Assert.ThrowsAsync<WorkerLinkException>(() => link);
        Assert.Same(failure, actual);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkAsync_EachRequestDelegatesAdmissionToRegistry()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<WorkerChannel> initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        _registry.Setup(registry => registry.LinkAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                if (Interlocked.Increment(ref calls) == 3)
                {
                    entered.SetResult();
                }
            })
            .Returns(initialization.Task);
        WorkerLinker linker = new(_registry.Object);
        Uri otherEndpoint = new("http://other-worker-proxy:50053");

        Task first = linker.LinkAsync(WorkerId, Endpoint);
        Task replay = linker.LinkAsync(WorkerId, Endpoint);
        Task other = linker.LinkAsync("other-worker", otherEndpoint);
        await entered.Task.WaitAsync(TestTimeout);

        Assert.False(first.IsCompleted);
        Assert.False(replay.IsCompleted);
        Assert.False(other.IsCompleted);
        initialization.SetResult(null!);
        await Task.WhenAll(first, replay, other).WaitAsync(TestTimeout);

        _registry.Verify(registry => registry.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _registry.Verify(registry => registry.LinkAsync("other-worker", otherEndpoint, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoRegistryLifecycleCalls();
    }

    [Fact]
    public async Task LinkAsync_CallerCancellation_CancelsRegistryAndPropagatesCallerToken()
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<CancellationToken> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<WorkerChannel> initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(registry => registry.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .Returns((string workerId, Uri endpoint, CancellationToken token) =>
            {
                entered.SetResult(token);

                return initialization.Task.WaitAsync(token);
            });
        WorkerLinker linker = new(_registry.Object);

        Task link = linker.LinkAsync(WorkerId, Endpoint, cancellation.Token);
        CancellationToken registryToken = await entered.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();

        OperationCanceledException actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => link.WaitAsync(TestTimeout));
        Assert.True(registryToken.IsCancellationRequested);
        Assert.Equal(cancellation.Token, actual.CancellationToken);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkAsync_Deadline_CancelsRegistryAndReportsUnavailable()
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<CancellationToken> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<WorkerChannel> initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(registry => registry.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .Returns((string workerId, Uri endpoint, CancellationToken token) =>
            {
                entered.SetResult(token);

                return initialization.Task.WaitAsync(token);
            });
        WorkerLinker linker = new(_registry.Object, TimeSpan.FromMilliseconds(100));

        Task link = linker.LinkAsync(WorkerId, Endpoint, cancellation.Token);
        CancellationToken registryToken = await entered.Task.WaitAsync(TestTimeout);
        WorkerLinkException actual = await Assert.ThrowsAsync<WorkerLinkException>(
            () => link.WaitAsync(TestTimeout));

        Assert.Equal(WorkerLinkFailureReason.Unavailable, actual.Reason);
        OperationCanceledException inner = Assert.IsAssignableFrom<OperationCanceledException>(actual.InnerException);
        Assert.Equal(registryToken, inner.CancellationToken);
        Assert.True(registryToken.IsCancellationRequested);
        Assert.False(cancellation.IsCancellationRequested);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkAsync_RawShutdown_ReportsRuntimeStopping()
    {
        ObjectDisposedException failure = new(PrivateDiagnostic);
        _registry.Setup(registry => registry.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .Throws(failure);
        WorkerLinker linker = new(_registry.Object);

        WorkerLinkException actual = await Assert.ThrowsAsync<WorkerLinkException>(
            () => linker.LinkAsync(WorkerId, Endpoint).WaitAsync(TestTimeout));

        Assert.Equal(WorkerLinkFailureReason.RuntimeStopping, actual.Reason);
        Assert.Same(failure, actual.InnerException);
        Assert.False(string.IsNullOrWhiteSpace(actual.Message));
        Assert.DoesNotContain(PrivateDiagnostic, actual.Message, StringComparison.Ordinal);
        VerifyOnlyLinkCall();
    }

    [Theory]
    [InlineData(WorkerLinkFailureReason.Conflict)]
    [InlineData(WorkerLinkFailureReason.WorkerTerminated)]
    [InlineData(WorkerLinkFailureReason.RuntimeStopping)]
    [InlineData(WorkerLinkFailureReason.Unavailable)]
    public async Task LinkAsync_TypedFailure_PreservesReasonAndInstance(WorkerLinkFailureReason reason)
    {
        WorkerLinkException failure = new(reason, "Safe admission detail.", new Exception(PrivateDiagnostic));
        _registry.Setup(registry => registry.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        WorkerLinker linker = new(_registry.Object);

        WorkerLinkException actual = await Assert.ThrowsAsync<WorkerLinkException>(
            () => linker.LinkAsync(WorkerId, Endpoint).WaitAsync(TestTimeout));

        Assert.Same(failure, actual);
        Assert.Equal(reason, actual.Reason);
        VerifyOnlyLinkCall();
    }

    [Theory]
    [InlineData(typeof(GrpcException))]
    [InlineData(typeof(WorkerRpcException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(SocketException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(ChannelClosedException))]
    [InlineData(typeof(OperationCanceledException))]
    public async Task LinkAsync_ExpectedFailure_ReportsSafeUnavailable(Type exceptionType)
    {
        Exception failure = exceptionType switch
        {
            Type type when type == typeof(GrpcException) => new GrpcException(new Status(StatusCode.Unavailable, PrivateDiagnostic)),
            Type type when type == typeof(WorkerRpcException) => new WorkerRpcException("Failure", PrivateDiagnostic, "Remote stack."),
            Type type when type == typeof(HttpRequestException) => new HttpRequestException(PrivateDiagnostic),
            Type type when type == typeof(IOException) => new IOException(PrivateDiagnostic),
            Type type when type == typeof(SocketException) => new SocketException((int)SocketError.ConnectionRefused),
            Type type when type == typeof(TimeoutException) => new TimeoutException(PrivateDiagnostic),
            Type type when type == typeof(ChannelClosedException) => new ChannelClosedException(PrivateDiagnostic),
            Type type when type == typeof(OperationCanceledException) => new OperationCanceledException(PrivateDiagnostic),
            _ => throw new ArgumentOutOfRangeException(nameof(exceptionType)),
        };
        _registry.Setup(registry => registry.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        WorkerLinker linker = new(_registry.Object);

        WorkerLinkException actual = await Assert.ThrowsAsync<WorkerLinkException>(
            () => linker.LinkAsync(WorkerId, Endpoint).WaitAsync(TestTimeout));

        Assert.Equal(WorkerLinkFailureReason.Unavailable, actual.Reason);
        Assert.Same(failure, actual.InnerException);
        Assert.False(string.IsNullOrWhiteSpace(actual.Message));
        Assert.DoesNotContain(PrivateDiagnostic, actual.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(failure.Message, actual.Message, StringComparison.Ordinal);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkAsync_ProgrammingError_PropagatesUnchanged()
    {
        InvalidOperationException failure = new("Programming error.");
        _registry.Setup(registry => registry.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        WorkerLinker linker = new(_registry.Object);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => linker.LinkAsync(WorkerId, Endpoint).WaitAsync(TestTimeout));

        Assert.Same(failure, actual);
        VerifyOnlyLinkCall();
    }

    private void VerifyOnlyLinkCall()
    {
        _registry.Verify(registry => registry.LinkAsync(WorkerId, Endpoint, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoRegistryLifecycleCalls();
    }

    private void VerifyNoRegistryLifecycleCalls()
    {
        _registry.Verify(registry => registry.UnlinkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _registry.Verify(registry => registry.DisposeAsync(), Times.Never);
        _registry.VerifyNoOtherCalls();
    }
}
