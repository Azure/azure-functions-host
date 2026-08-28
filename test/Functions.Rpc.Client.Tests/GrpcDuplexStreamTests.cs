// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public partial class GrpcDuplexStreamTests
{
    [Fact]
    public async Task WriteAndReadAdaptSdkStreams()
    {
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new();
        GrpcDuplexStream<string> stream =
            streams.Call.AsDuplexStream(callLifetimeSource, resource);

        await stream.Writer.WriteAsync("request");
        await streams.SendResponseAsync("response");
        streams.CompleteResponses();
        List<string> responses = [];
        await foreach (string response in stream.Reader.ReadAllAsync())
        {
            responses.Add(response);
        }
        await streams.RequestCompleted.WaitAsync(TimeSpan.FromSeconds(10));
        await stream.DisposeAsync();

        Assert.Equal(["request"], streams.WrittenMessages);
        Assert.Equal(["response"], responses);
    }

    [Fact]
    public async Task DisposeIsIdempotentAndReleasesOwnedResources()
    {
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new();
        GrpcDuplexStream<string> stream =
            streams.Call.AsDuplexStream(callLifetimeSource, resource);

        await Task.WhenAll(stream.DisposeAsync().AsTask(), stream.DisposeAsync().AsTask());

        Assert.Equal(1, streams.DisposeCount);
        Assert.Equal(1, resource.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => callLifetimeSource.Token);
    }

    [Fact]
    public async Task DisposeAbortsBlockedWrite()
    {
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token, blockWrites: true);
        TrackingDisposable resource = new();
        GrpcDuplexStream<string> stream =
            streams.Call.AsDuplexStream(callLifetimeSource, resource);
        await stream.Writer.WriteAsync("request");
        await streams.WriteAttempts.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        await stream.DisposeAsync();

        Assert.Equal(1, streams.DisposeCount);
        Assert.Equal(1, resource.DisposeCount);
    }

    [Fact]
    public async Task DisposeReportsCleanupFailure()
    {
        InvalidOperationException expected = new("resource cleanup failed");
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new(expected);
        GrpcDuplexStream<string> stream =
            streams.Call.AsDuplexStream(callLifetimeSource, resource);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() => stream.DisposeAsync().AsTask());

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task ResponseFailureFaultsBothChannelBoundaries()
    {
        InvalidOperationException expected = new("response failure");
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new();
        GrpcDuplexStream<string> stream =
            streams.Call.AsDuplexStream(callLifetimeSource, resource);

        streams.CompleteResponses(expected);

        InvalidOperationException readFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => stream.Reader.Completion);
        ChannelClosedException writeFailure = await Assert.ThrowsAsync<ChannelClosedException>(() =>
            stream.Writer.WriteAsync("late request").AsTask());
        await stream.DisposeAsync();

        Assert.Same(expected, readFailure);
        Assert.Same(expected, writeFailure.InnerException);
    }

    [Fact]
    public async Task RequestFailureFaultsBothChannelBoundaries()
    {
        InvalidOperationException expected = new("request failure");
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token, blockWrites: true);
        TrackingDisposable resource = new();
        GrpcDuplexStream<string> stream =
            streams.Call.AsDuplexStream(callLifetimeSource, resource);
        await stream.Writer.WriteAsync("request");
        await streams.WriteAttempts.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        streams.FailWrites(expected);

        InvalidOperationException readFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => stream.Reader.Completion);
        ChannelClosedException writeFailure = await Assert.ThrowsAsync<ChannelClosedException>(() =>
            stream.Writer.WriteAsync("late request").AsTask());
        await stream.DisposeAsync();

        Assert.Same(expected, readFailure);
        Assert.Same(expected, writeFailure.InnerException);
    }

    [Fact]
    public async Task ReadCancellationDoesNotDisposeStream()
    {
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new();
        await using GrpcDuplexStream<string> stream =
            streams.Call.AsDuplexStream(callLifetimeSource, resource);
        using CancellationTokenSource readSource = new();
        Task<string> pendingRead = stream.Reader.ReadAsync(readSource.Token).AsTask();

        readSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingRead);
        Assert.Equal(0, streams.DisposeCount);
    }
}
