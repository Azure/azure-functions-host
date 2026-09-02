// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class WorkerEndpointReadinessProbeTests
{
    [Fact]
    public async Task WaitForReadyAsync_ListeningDestination_CachesSuccess()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        Uri destination = new($"http://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}");
        WorkerEndpointReadinessProbe probe = CreateProbe();

        Assert.True(await probe.WaitForReadyAsync(destination, CancellationToken.None));
        listener.Stop();
        Assert.True(probe.IsKnownReady(destination));
        Assert.True(await probe.WaitForReadyAsync(destination, CancellationToken.None));
    }

    [Fact]
    public async Task WaitForReadyAsync_CallerCancellation_Throws()
    {
        WorkerEndpointReadinessProbe probe = CreateProbe();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.WaitForReadyAsync(new Uri("http://localhost:1"), cancellation.Token).AsTask());
    }

    [Fact]
    public async Task WaitForReadyAsync_UnresolvableDestination_ReturnsFalse()
    {
        WorkerEndpointReadinessProbe probe = CreateProbe();

        Assert.False(await probe.WaitForReadyAsync(
            new Uri("http://host.invalid"),
            CancellationToken.None));
    }

    [Fact]
    public async Task WaitForReadyAsync_DestinationBindsDuringBudget_ReturnsTrue()
    {
        int port = GetUnusedPort();
        WorkerEndpointReadinessProbe probe = CreateProbe(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromSeconds(2));
        using TcpListener listener = new(IPAddress.Loopback, port);
        ValueTask<bool> ready = probe.WaitForReadyAsync(
            new Uri($"http://localhost:{port}"),
            CancellationToken.None);
        await Task.Delay(100);
        listener.Start();

        Assert.True(await ready);
    }

    [Fact]
    public async Task WaitForReadyAsync_DestinationNeverBinds_ReturnsFalse()
    {
        int port = GetUnusedPort();
        WorkerEndpointReadinessProbe probe = CreateProbe(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(100));

        Assert.False(await probe.WaitForReadyAsync(
            new Uri($"http://localhost:{port}"),
            CancellationToken.None));
    }

    private static int GetUnusedPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static WorkerEndpointReadinessProbe CreateProbe(
        TimeSpan? retryDelay = null,
        TimeSpan? totalTimeout = null)
    {
        WorkerEndpointReadinessProbeOptions options = new()
        {
            RetryDelay = retryDelay ?? TimeSpan.FromMilliseconds(25),
            TotalTimeout = totalTimeout ?? TimeSpan.FromSeconds(5)
        };

        return new WorkerEndpointReadinessProbe(
            Options.Create(options),
            NullLogger<WorkerEndpointReadinessProbe>.Instance);
    }
}
