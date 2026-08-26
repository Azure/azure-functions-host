// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public partial class GrpcDuplexCallTests
{
    [Fact]
    public async Task WriteAndReadAdaptSdkStreams()
    {
        using CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new();
        await using GrpcDuplexCall<string, string> call =
            new(streams.Call, callLifetimeSource, resource, new FakeLogger<GrpcDuplexCall<string, string>>());

        await call.WriteAsync("request");
        await streams.SendResponseAsync("response");
        streams.CompleteResponses();
        List<string> responses = [];
        await foreach (string response in call.ReadAllAsync())
        {
            responses.Add(response);
        }

        Assert.Equal(["request"], streams.WrittenMessages);
        Assert.Equal(["response"], responses);
    }

    [Fact]
    public async Task DisposeIsIdempotentAndReleasesOwnedResources()
    {
        CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new();
        GrpcDuplexCall<string, string> call = new(streams.Call, callLifetimeSource, resource,
            new FakeLogger<GrpcDuplexCall<string, string>>());

        await Task.WhenAll(call.DisposeAsync().AsTask(), call.DisposeAsync().AsTask());

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
        GrpcDuplexCall<string, string> call = new(streams.Call, callLifetimeSource, resource,
            new FakeLogger<GrpcDuplexCall<string, string>>());
        Task writeTask = call.WriteAsync("request");
        await streams.WriteAttempts.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        await call.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writeTask);
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
        FakeLogger<GrpcDuplexCall<string, string>> logger = new();
        GrpcDuplexCall<string, string> call = new(streams.Call, callLifetimeSource, resource, logger);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() => call.DisposeAsync().AsTask());

        Assert.Same(expected, actual);
        Assert.Same(expected, logger.Collector.LatestRecord.Exception);
        Assert.Equal(LogLevel.Warning, logger.Collector.LatestRecord.Level);
    }

    [Fact]
    public async Task ReadCancellationDoesNotDisposeCall()
    {
        using CancellationTokenSource callLifetimeSource = new();
        MockDuplexStream<string> streams = new(callLifetimeSource.Token);
        TrackingDisposable resource = new();
        await using GrpcDuplexCall<string, string> call =
            new(streams.Call, callLifetimeSource, resource, new FakeLogger<GrpcDuplexCall<string, string>>());
        using CancellationTokenSource readSource = new();
        await using IAsyncEnumerator<string> responses = call.ReadAllAsync(readSource.Token).GetAsyncEnumerator();
        Task<bool> pendingRead = responses.MoveNextAsync().AsTask();

        readSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingRead);
        Assert.Equal(0, streams.DisposeCount);
    }
}
