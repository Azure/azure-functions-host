// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public partial class GrpcDuplexChannelTests
{
    [Fact]
    public async Task WriteAndReadAdaptSdkStreams()
    {
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new();
        GrpcDuplexChannel<string> channel =
            new(streams.Call, callLifetimeSource, resource, new FakeLogger<GrpcDuplexChannel<string>>());

        await channel.Writer.WriteAsync("request");
        await streams.SendResponseAsync("response");
        streams.CompleteResponses();
        List<string> responses = [];
        await foreach (string response in channel.Reader.ReadAllAsync())
        {
            responses.Add(response);
        }
        await streams.RequestCompleted.WaitAsync(TimeSpan.FromSeconds(10));
        await channel.DisposeAsync();

        Assert.Equal(["request"], streams.WrittenMessages);
        Assert.Equal(["response"], responses);
    }

    [Fact]
    public async Task DisposeIsIdempotentAndReleasesOwnedResources()
    {
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new();
        GrpcDuplexChannel<string> channel = new(streams.Call, callLifetimeSource, resource,
            new FakeLogger<GrpcDuplexChannel<string>>());

        await Task.WhenAll(channel.DisposeAsync().AsTask(), channel.DisposeAsync().AsTask());

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
        GrpcDuplexChannel<string> channel = new(streams.Call, callLifetimeSource, resource,
            new FakeLogger<GrpcDuplexChannel<string>>());
        await channel.Writer.WriteAsync("request");
        await streams.WriteAttempts.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        await channel.DisposeAsync();

        Assert.Equal(1, streams.DisposeCount);
        Assert.Equal(1, resource.DisposeCount);
    }

    [Fact]
    public async Task DisposeLogsAndReportsCleanupFailure()
    {
        InvalidOperationException expected = new("resource cleanup failed");
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new(expected);
        FakeLogger<GrpcDuplexChannel<string>> logger = new();
        GrpcDuplexChannel<string> channel = new(streams.Call, callLifetimeSource, resource, logger);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() => channel.DisposeAsync().AsTask());

        Assert.Same(expected, actual);
        Assert.Same(expected, logger.Collector.LatestRecord.Exception);
        Assert.Equal(LogLevel.Warning, logger.Collector.LatestRecord.Level);
    }

    [Fact]
    public async Task ResponseFailureFaultsBothChannelBoundaries()
    {
        InvalidOperationException expected = new("response failure");
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new();
        GrpcDuplexChannel<string> channel = new(streams.Call, callLifetimeSource, resource,
            new FakeLogger<GrpcDuplexChannel<string>>());

        streams.CompleteResponses(expected);

        InvalidOperationException readFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => channel.Reader.Completion);
        ChannelClosedException writeFailure = await Assert.ThrowsAsync<ChannelClosedException>(() =>
            channel.Writer.WriteAsync("late request").AsTask());
        await channel.DisposeAsync();

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
        GrpcDuplexChannel<string> channel = new(streams.Call, callLifetimeSource, resource,
            new FakeLogger<GrpcDuplexChannel<string>>());
        await channel.Writer.WriteAsync("request");
        await streams.WriteAttempts.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        streams.FailWrites(expected);

        InvalidOperationException readFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => channel.Reader.Completion);
        ChannelClosedException writeFailure = await Assert.ThrowsAsync<ChannelClosedException>(() =>
            channel.Writer.WriteAsync("late request").AsTask());
        await channel.DisposeAsync();

        Assert.Same(expected, readFailure);
        Assert.Same(expected, writeFailure.InnerException);
    }

    [Fact]
    public async Task ReadCancellationDoesNotDisposeChannel()
    {
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new();
        await using GrpcDuplexChannel<string> channel =
            new(streams.Call, callLifetimeSource, resource, new FakeLogger<GrpcDuplexChannel<string>>());
        using CancellationTokenSource readSource = new();
        Task<string> pendingRead = channel.Reader.ReadAsync(readSource.Token).AsTask();

        readSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingRead);
        Assert.Equal(0, streams.DisposeCount);
    }
}
