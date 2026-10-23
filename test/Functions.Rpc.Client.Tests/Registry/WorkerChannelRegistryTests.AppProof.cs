// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("malformed")]
    [InlineData("tampered")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("wrong-key")]
    [InlineData("wrong-app")]
    [InlineData("wrong-worker")]
    [InlineData("worker-case")]
    [InlineData("missing-worker")]
    [InlineData("wrong-purpose")]
    [InlineData("caller-token")]
    public async Task LinkAsync_InvalidReverseProofNeverPublishesAndCleansTransport(string failure)
    {
        using CancellationTokenSource timeout = new(GrpcTestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(GrpcWorkerId);
        await using RpcClientFactory clients = new(NullLogger<RpcClientFactory>.Instance);
        await using WorkerChannelRegistry registry = CreateGrpcRegistry(clients);
        Task<WorkerLinkResult> link = registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        await worker.InitializationStarted.WaitAsync(timeout.Token);
        worker.CompleteInitialization(appProof: InvalidAppProof(failure),
            includeProof: !string.Equals(failure, "missing", StringComparison.Ordinal));

        WorkerLinkException error = await Assert.ThrowsAsync<WorkerLinkException>(() => link.WaitAsync(timeout.Token));

        Assert.Equal(WorkerLinkFailureReason.Unavailable, error.Reason);
        Assert.Empty(registry.GetInitializedChannels());
        await worker.Disconnected.WaitAsync(timeout.Token);
        Assert.DoesNotContain(RuntimeWorkerTestData.AppKey, error.Message, StringComparison.Ordinal);
    }

    private static string InvalidAppProof(string failure)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        byte[] key = Convert.FromHexString(RuntimeWorkerTestData.AppKey);
        string audience = RuntimeWorkerAuthentication.AppAudience("test-app");
        string purpose = RuntimeWorkerAuthentication.AppPurpose;

        return failure switch
        {
            "empty" => string.Empty,
            "malformed" => "invalid-proof",
            "tampered" => RuntimeWorkerTestData.CreateAppProof(GrpcWorkerId) + "A",
            "expired" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now.AddMinutes(-6), GrpcWorkerId),
            "future" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now.AddMinutes(1), GrpcWorkerId),
            "wrong-key" => RuntimeWorkerAuthentication.CreateToken(new byte[32], audience, purpose, now, GrpcWorkerId),
            "wrong-app" => RuntimeWorkerAuthentication.CreateToken(key, audience + "-other-app", purpose, now, GrpcWorkerId),
            "wrong-worker" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now, "different-worker"),
            "worker-case" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now, GrpcWorkerId.ToUpperInvariant()),
            "missing-worker" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now),
            "wrong-purpose" => RuntimeWorkerAuthentication.CreateToken(key, audience, "wrong-purpose", now, GrpcWorkerId),
            "caller-token" => RuntimeWorkerTestData.CreateCredentials(GrpcWorkerId).CreateHeaders()[RuntimeWorkerAuthentication.WorkerTokenHeader],
            _ => RuntimeWorkerTestData.CreateAppProof(GrpcWorkerId)
        };
    }
}
