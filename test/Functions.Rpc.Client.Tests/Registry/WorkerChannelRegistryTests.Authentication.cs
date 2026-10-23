// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkAsync_DifferentCredentialOrGrpcOriginConflictsWithoutReinitialization(bool changeKey)
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        Uri grpcEndpoint = CreateEndpoint("worker");
        string differentKey = Convert.ToBase64String(SHA256.HashData("different-worker-key"u8));
        Task<WorkerLinkResult> first = registry.LinkAsync("worker", grpcEndpoint, RuntimeWorkerTestData.WorkerKey);
        await channel.StartEntered.WaitAsync(TestTimeout);

        foreach (bool ready in new[] { false, true })
        {
            if (ready)
            {
                channel.AllowStart();
                await first.WaitAsync(TestTimeout);
            }

            WorkerLinkException failure = await Assert.ThrowsAsync<WorkerLinkException>(() =>
                registry.LinkAsync("worker", changeKey ? grpcEndpoint : new Uri("https://different-proxy:48802"),
                    changeKey ? differentKey : RuntimeWorkerTestData.WorkerKey));
            Assert.Equal(WorkerLinkFailureReason.Conflict, failure.Reason);
            Assert.DoesNotContain(differentKey, failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(RuntimeWorkerTestData.WorkerKey, failure.Message, StringComparison.Ordinal);
        }

        Assert.Single(harness.Transports);
    }
}
