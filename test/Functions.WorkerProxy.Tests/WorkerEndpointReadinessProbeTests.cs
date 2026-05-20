// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Azure.Functions.WorkerProxy;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class WorkerEndpointReadinessProbeTests
{
    // Pre-bound listener: the probe must succeed on the first attempt and cache the
    // result so subsequent calls are a no-op fast-path.
    [Fact]
    public async Task WaitForReadyAsync_PortAlreadyListening_ReturnsTrueImmediately()
    {
        using var listener = StartLoopbackListener(out int port);
        var probe = CreateProbe();
        string destination = $"http://127.0.0.1:{port}";

        bool ready = await probe.WaitForReadyAsync(destination, CancellationToken.None);

        Assert.True(ready);
        Assert.True(probe.IsKnownReady(destination));
    }

    // The whole point of the probe: a destination that comes online after the probe
    // starts must be picked up by the retry loop. We deliberately delay binding so
    // the first attempt fails (connection refused) but a subsequent one succeeds.
    [Fact]
    public async Task WaitForReadyAsync_PortBoundAfterShortDelay_Succeeds()
    {
        int port = GetUnusedTcpPort();
        var probe = CreateProbe(totalTimeoutMs: 5_000);
        var destination = $"http://127.0.0.1:{port}";

        TcpListener? listener = null;
        try
        {
            var bindTask = Task.Run(async () =>
            {
                await Task.Delay(100);
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
            });

            var sw = Stopwatch.StartNew();
            bool ready = await probe.WaitForReadyAsync(destination, CancellationToken.None);
            sw.Stop();

            await bindTask;

            Assert.True(ready);
            Assert.True(probe.IsKnownReady(destination));
            // The probe should have spent meaningfully less than the full budget.
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
                $"Probe took {sw.ElapsedMilliseconds} ms; expected fast pick-up after bind.");
        }
        finally
        {
            listener?.Stop();
        }
    }

    // Timeout path: when the destination never accepts connections, the probe must
    // give up and return false within (approximately) the configured budget — and
    // crucially must NOT cache the destination as ready.
    [Fact]
    public async Task WaitForReadyAsync_NeverListening_ReturnsFalseWithinBudget()
    {
        int port = GetUnusedTcpPort();
        var probe = CreateProbe(totalTimeoutMs: 200);
        var destination = $"http://127.0.0.1:{port}";

        var sw = Stopwatch.StartNew();
        bool ready = await probe.WaitForReadyAsync(destination, CancellationToken.None);
        sw.Stop();

        Assert.False(ready);
        Assert.False(probe.IsKnownReady(destination));
        // Generous upper bound: 200 ms budget + retry-delay slop + scheduler jitter.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
            $"Probe took {sw.ElapsedMilliseconds} ms; expected ~200 ms.");
    }

    // After a successful probe, subsequent calls must be a true fast-path (no
    // socket syscalls, instant return) even if the destination later disappears.
    [Fact]
    public async Task WaitForReadyAsync_CachedDestination_ShortCircuits()
    {
        int port;
        using (var listener = StartLoopbackListener(out port))
        {
            var probe = CreateProbe();
            string destination = $"http://127.0.0.1:{port}";

            Assert.True(await probe.WaitForReadyAsync(destination, CancellationToken.None));

            // Stop the listener — proves the second call did NOT touch the network.
            listener.Stop();

            var sw = Stopwatch.StartNew();
            bool ready = await probe.WaitForReadyAsync(destination, CancellationToken.None);
            sw.Stop();

            Assert.True(ready);
            Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(50),
                $"Cached lookup took {sw.ElapsedMilliseconds} ms; expected near-zero.");
        }
    }

    // Cancellation: in production the request's cancellation token plumbs through.
    // An already-cancelled token must abort the probe immediately with OCE rather
    // than burning the full timeout budget.
    [Fact]
    public async Task WaitForReadyAsync_CancelledToken_Throws()
    {
        int port = GetUnusedTcpPort();
        var probe = CreateProbe(totalTimeoutMs: 5_000);
        var destination = $"http://127.0.0.1:{port}";

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await probe.WaitForReadyAsync(destination, cts.Token));
    }

    // A worker that hasn't advertised an HttpUri yet would be filtered by the
    // resolver, but we still defend the probe against unparseable inputs so a
    // misconfigured override never crashes the proxy.
    [Fact]
    public async Task WaitForReadyAsync_InvalidDestination_ReturnsFalse()
    {
        var probe = CreateProbe();

        bool ready = await probe.WaitForReadyAsync("not-a-url", CancellationToken.None);

        Assert.False(ready);
    }

    // Hostname resolution: localhost must be treated as loopback even when no
    // listener is bound to the IPv6 form. The probe should reach the IPv4 listener.
    [Fact]
    public async Task WaitForReadyAsync_LocalhostHostname_Resolves()
    {
        using var listener = StartLoopbackListener(out int port);
        var probe = CreateProbe();
        string destination = $"http://localhost:{port}";

        bool ready = await probe.WaitForReadyAsync(destination, CancellationToken.None);

        Assert.True(ready);
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkerEndpointReadinessProbe(
            null!,
            retryDelay: TimeSpan.FromMilliseconds(1),
            totalTimeout: TimeSpan.FromMilliseconds(100)));
    }

    private static WorkerEndpointReadinessProbe CreateProbe(int totalTimeoutMs = 1_000) =>
        new(
            NullLogger<WorkerEndpointReadinessProbe>.Instance,
            retryDelay: TimeSpan.FromMilliseconds(1),
            totalTimeout: TimeSpan.FromMilliseconds(totalTimeoutMs));

    private static TcpListener StartLoopbackListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;

        return listener;
    }

    private static int GetUnusedTcpPort()
    {
        using var temp = new TcpListener(IPAddress.Loopback, 0);
        temp.Start();
        int port = ((IPEndPoint)temp.LocalEndpoint).Port;
        temp.Stop();

        return port;
    }
}
