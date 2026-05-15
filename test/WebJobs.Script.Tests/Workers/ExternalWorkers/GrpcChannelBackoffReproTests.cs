// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.ExternalWorkers
{
    /// <summary>
    /// Diagnostic / reproduction tests that empirically characterise the
    /// delay observed when a <see cref="GrpcChannel.ConnectAsync"/> call is
    /// made against an endpoint whose listener is not yet accepting
    /// connections.
    ///
    /// <para>
    /// <b>Headline finding</b> (Linux, the production OS): the ~1-second link
    /// delay observed in field telemetry IS gRPC's
    /// <c>ExponentialBackoffPolicy</c>. The first socket connect fails fast
    /// with <c>Connection refused</c>; the subchannel enters
    /// <c>TransientFailure</c> and waits the policy's first backoff
    /// (<see cref="GrpcChannelOptions.InitialReconnectBackoff"/>, default
    /// <c>1 s</c>, ±20% jitter) before retrying. When run in Docker on .NET 10
    /// + <c>Grpc.Net.Client</c> 2.55.0 with a closed loopback port and a
    /// listener that comes up 100–900 ms later, <see cref="GrpcChannel.ConnectAsync"/>
    /// reliably returns in ~800–1200 ms — matching the field histogram.
    /// Setting <see cref="GrpcChannelOptions.InitialReconnectBackoff"/> to
    /// 50 / 100 / 250 / 1000 ms scales the connect time approximately linearly
    /// (~50 / ~100 / ~250 / ~1000 ms) — i.e., it is an effective tuning knob
    /// on Linux.
    /// </para>
    ///
    /// <para>
    /// <b>Windows behaviour is different (and misleading)</b>. On Windows
    /// loopback, <see cref="Socket.ConnectAsync(System.Net.EndPoint, CancellationToken)"/>
    /// to a port with no listener blocks for ~500 ms inside the OS TCP stack
    /// (kernel-level SYN retransmits) and ultimately succeeds when the
    /// listener becomes available — without gRPC ever observing the first
    /// attempt as failed. The captured gRPC log shows a single
    /// <c>connecting socket</c> entry and no <c>starting connect backoff</c>.
    /// Tests run on Windows therefore characterise <i>OS</i> retransmit
    /// behaviour, not gRPC backoff, and produce different (faster) numbers
    /// than production. Use Linux/Docker runs for production-relevant
    /// measurements.
    /// </para>
    ///
    /// <para>
    /// <b>Production implication</b>: the gRPC <c>InitialReconnectBackoff</c>
    /// (default 1&#x202F;s) is the dominant contributor to the observed link
    /// latency when the worker proxy is not yet listening at link time.
    /// Lowering it to ~100–200 ms via
    /// <see cref="GrpcChannelOptions.InitialReconnectBackoff"/> is a viable
    /// fast-path mitigation. "Throw the channel away and create a fresh one"
    /// (the <c>WorkerConnectionService</c> for-loop pattern) also works: a
    /// fresh subchannel starts at <c>attempt = 0</c> with no prior backoff
    /// state.
    /// </para>
    ///
    /// <para>
    /// Upstream references (Grpc.Net.Client v2.55.0):
    /// <list type="bullet">
    ///   <item><description>
    ///     <see href="https://github.com/grpc/grpc-dotnet/blob/v2.55.0/src/Grpc.Net.Client/GrpcChannel.cs#L42"/> —
    ///     <c>DefaultInitialReconnectBackoffTicks = TimeSpan.TicksPerSecond * 1</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <see href="https://github.com/grpc/grpc-dotnet/blob/v2.55.0/src/Grpc.Net.Client/Balancer/Subchannel.cs"/> —
    ///     <c>ConnectTransportAsync</c> for-loop where <c>StartingConnectBackoff</c> is logged. We confirmed via captured Trace logs on Linux that this loop runs (entries: <c>starting connect backoff of 00:00:01.x</c>).
    ///   </description></item>
    ///   <item><description>
    ///     <see href="https://github.com/grpc/grpc-dotnet/blob/v2.55.0/src/Grpc.Net.Client/Balancer/Internal/SocketConnectivitySubchannelTransport.cs"/> —
    ///     bare <c>socket.ConnectAsync</c>; no HTTP/2 or TLS is required for <c>ConnectivityState.Ready</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <see href="https://github.com/grpc/grpc-dotnet/blob/v2.55.0/src/Grpc.Net.Client/Balancer/Internal/ExponentialBackoffPolicy.cs"/> —
    ///     <c>Multiplier = 1.6</c>, <c>Jitter = 0.2</c>. The second backoff (~1.6&#x202F;s ± 20%) is observable when the listener comes up after T+1&#x202F;s.
    ///   </description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// "Server" in these tests is a plain <see cref="TcpListener"/>; no real
    /// gRPC server is needed because the subchannel reaches
    /// <c>ConnectivityState.Ready</c> as soon as the TCP handshake completes.
    /// </para>
    /// </summary>
    [Trait("Category", "E2E")]
    public class GrpcChannelBackoffReproTests
    {
        // The minimum end-to-end delay we expect when the first connect
        // attempt finds the listener not yet up. On Linux + Grpc.Net.Client
        // 2.55.0 this is dominated by ExponentialBackoffPolicy's first
        // NextBackoff() (default 1 s * (1 - 0.2 jitter) = 800 ms floor).
        // On Windows loopback it is dominated by the OS TCP SYN retransmit
        // timer (~500 ms first retry). We use 400 ms as the lower bound so
        // the assertion passes on either OS and still proves the
        // production-relevant claim ("the channel cannot react immediately
        // to a listener that becomes available after the first failed
        // attempt").
        private static readonly TimeSpan BackoffLowerBound = TimeSpan.FromMilliseconds(400);

        // Generous upper bound covering the Linux ~1 s ExponentialBackoffPolicy
        // first backoff + jitter + scheduling slack.
        private static readonly TimeSpan BackoffUpperBound = TimeSpan.FromMilliseconds(2000);

        // Generous overall budget so a CI hiccup does not deadlock the test.
        private static readonly TimeSpan TestBudget = TimeSpan.FromSeconds(10);

        // Threshold for "fast" connects when the listener is already up.
        private static readonly TimeSpan FastConnectUpperBound = TimeSpan.FromMilliseconds(500);

        // Delay before bringing the listener up in the "smoking gun" test.
        // Chosen well below the default 1 s initial backoff so the listener
        // is clearly available before the backoff timer expires.
        private static readonly TimeSpan ListenerStartDelay = TimeSpan.FromMilliseconds(200);

        private readonly ITestOutputHelper _output;

        public GrpcChannelBackoffReproTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Control: when the listener is already accepting connections at T = 0,
        /// <see cref="GrpcChannel.ConnectAsync"/> should complete quickly because
        /// the first TCP connect attempt succeeds and no backoff is engaged.
        /// </summary>
        [Fact]
        public async Task ConnectAsync_FastPath_WhenListenerImmediatelyAvailable()
        {
            int port = GetFreeLoopbackPort();

            using var listener = StartListener(port);

            using var channel = CreateChannel(port);

            using var cts = new CancellationTokenSource(TestBudget);
            var sw = Stopwatch.StartNew();
            await channel.ConnectAsync(cts.Token);
            sw.Stop();

            _output.WriteLine($"Fast-path ConnectAsync elapsed: {sw.ElapsedMilliseconds} ms");

            Assert.True(
                sw.Elapsed < FastConnectUpperBound,
                $"Expected < {FastConnectUpperBound.TotalMilliseconds} ms when listener is immediately available; actual {sw.ElapsedMilliseconds} ms.");
        }

        /// <summary>
        /// Smoking gun: when the listener comes up at T ≈ 200 ms but the port
        /// is closed at T = 0, <see cref="GrpcChannel.ConnectAsync"/> takes
        /// substantially longer than the listener-startup time. On Linux
        /// (production) this is gRPC's <c>ExponentialBackoffPolicy</c> first
        /// backoff (~1 s ± 20%). On Windows loopback it is OS-level TCP SYN
        /// retransmit (~500 ms). Either way the call cannot react
        /// immediately to the listener becoming available.
        /// </summary>
        [Fact]
        public async Task ConnectAsync_WaitsForBackoff_WhenListenerComesUpAfterFirstAttempt()
        {
            int port = GetFreeLoopbackPort();

            using var channel = CreateChannel(port);

            // Bring the listener up well before the default 1 s backoff timer
            // would expire. This proves the "stuck in backoff" hypothesis:
            // even though the listener is available at ~200 ms, ConnectAsync
            // cannot detect it and continues sleeping.
            TcpListener? listener = null;
            try
            {
                var listenerTask = Task.Run(async () =>
                {
                    await Task.Delay(ListenerStartDelay);
                    listener = StartListener(port);
                });

                using var cts = new CancellationTokenSource(TestBudget);
                var sw = Stopwatch.StartNew();
                await channel.ConnectAsync(cts.Token);
                sw.Stop();

                await listenerTask;

                _output.WriteLine($"Listener delay: {ListenerStartDelay.TotalMilliseconds} ms");
                _output.WriteLine($"Backoff-dominated ConnectAsync elapsed: {sw.ElapsedMilliseconds} ms");

                string lowerBoundMessage = $"Expected >= {BackoffLowerBound.TotalMilliseconds} ms (substantially more than the {ListenerStartDelay.TotalMilliseconds} ms listener-startup time, indicating gRPC could not react immediately to the listener becoming available); actual {sw.ElapsedMilliseconds} ms.";
                Assert.True(sw.Elapsed >= BackoffLowerBound, lowerBoundMessage);

                string upperBoundMessage = $"Expected <= {BackoffUpperBound.TotalMilliseconds} ms (covers the documented ~1 s default initial backoff + jitter + scheduling slack); actual {sw.ElapsedMilliseconds} ms. If this fails the test is likely under environmental load rather than the hypothesis being wrong.";
                Assert.True(sw.Elapsed <= BackoffUpperBound, upperBoundMessage);
            }
            finally
            {
                listener?.Stop();
            }
        }

        /// <summary>
        /// Mitigation control: a brand-new <see cref="GrpcChannel"/> against the
        /// same endpoint connects quickly even after a previous channel
        /// experienced a failed connect attempt against that endpoint. This
        /// confirms that recreating the channel is sufficient to bypass the
        /// per-subchannel backoff state — which is exactly what the retry
        /// for-loop in <c>WorkerConnectionService</c> does on each iteration.
        /// </summary>
        [Fact]
        public async Task ConnectAsync_OnFreshChannel_ConnectsFast_AfterPriorChannelFailed()
        {
            int port = GetFreeLoopbackPort();

            // First channel: attempt to connect against a closed port and
            // give up via a short cancellation so the test does not pay the
            // full backoff cost. We do not assert anything about this call;
            // it exists only to demonstrate that channel 1 has had a failed
            // attempt and would still be in backoff if we kept it.
            using (var channel1 = CreateChannel(port))
            {
                using var shortCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                try
                {
                    await channel1.ConnectAsync(shortCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected: the closed-port attempt cannot succeed in 100 ms.
                }
            }

            // Now bring the listener up.
            using var listener = StartListener(port);

            // Fresh channel: assert it connects quickly. A new subchannel
            // means a fresh backoff policy starting at attempt 0, so the
            // first TryConnectAsync runs immediately and succeeds.
            using var channel2 = CreateChannel(port);

            using var cts = new CancellationTokenSource(TestBudget);
            var sw = Stopwatch.StartNew();
            await channel2.ConnectAsync(cts.Token);
            sw.Stop();

            _output.WriteLine($"Fresh-channel ConnectAsync elapsed: {sw.ElapsedMilliseconds} ms");

            Assert.True(
                sw.Elapsed < FastConnectUpperBound,
                $"Expected < {FastConnectUpperBound.TotalMilliseconds} ms on a fresh channel (no prior failure state); actual {sw.ElapsedMilliseconds} ms.");
        }

        /// <summary>
        /// Characterisation: measure how long <see cref="GrpcChannel.ConnectAsync"/>
        /// actually takes when the listener becomes available at various offsets
        /// after the first failed connect attempt. This is informational — it
        /// runs always and reports timings, so the findings can be read off the
        /// xUnit output regardless of whether any threshold was met.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(100)]
        [InlineData(300)]
        [InlineData(600)]
        [InlineData(900)]
        [InlineData(1200)]
        [InlineData(1500)]
        public async Task Characterise_ConnectAsync_VsListenerStartDelay(int listenerDelayMs)
        {
            int port = GetFreeLoopbackPort();

            using var channel = CreateChannel(port);

            TcpListener? listener = null;
            try
            {
                var listenerTask = Task.Run(async () =>
                {
                    if (listenerDelayMs > 0)
                    {
                        await Task.Delay(listenerDelayMs);
                    }
                    listener = StartListener(port);
                });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var sw = Stopwatch.StartNew();
                await channel.ConnectAsync(cts.Token);
                sw.Stop();

                await listenerTask;

                _output.WriteLine(
                    $"Listener delay: {listenerDelayMs,5} ms | ConnectAsync elapsed: {sw.ElapsedMilliseconds,5} ms | Gap (connect - listener): {sw.ElapsedMilliseconds - listenerDelayMs,5} ms");
            }
            finally
            {
                listener?.Stop();
            }
        }

        /// <summary>
        /// Characterisation with a configured <c>InitialReconnectBackoff</c>.
        /// Confirms whether the publicly-documented option actually controls
        /// the time spent between attempts, and gives us a target value to use
        /// on the production channel if we want faster recovery.
        /// </summary>
        [Theory]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(250)]
        [InlineData(1000)]
        public async Task Characterise_ConnectAsync_WithConfiguredInitialReconnectBackoff(int initialBackoffMs)
        {
            int port = GetFreeLoopbackPort();

            // Listener comes up just after the first attempt is likely to have
            // failed. With a small InitialReconnectBackoff the second attempt
            // should fire shortly after the listener is available.
            const int listenerDelayMs = 50;

            var options = new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) },
                InitialReconnectBackoff = TimeSpan.FromMilliseconds(initialBackoffMs)
            };

            using var channel = GrpcChannel.ForAddress(new Uri($"http://127.0.0.1:{port}"), options);

            TcpListener? listener = null;
            try
            {
                var listenerTask = Task.Run(async () =>
                {
                    await Task.Delay(listenerDelayMs);
                    listener = StartListener(port);
                });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var sw = Stopwatch.StartNew();
                await channel.ConnectAsync(cts.Token);
                sw.Stop();

                await listenerTask;

                _output.WriteLine(
                    $"InitialReconnectBackoff: {initialBackoffMs,5} ms | Listener delay: {listenerDelayMs,3} ms | ConnectAsync elapsed: {sw.ElapsedMilliseconds,5} ms");
            }
            finally
            {
                listener?.Stop();
            }
        }

        private static int GetFreeLoopbackPort()
        {
            // Bind a listener on port 0 (kernel chooses), record the port,
            // then stop the listener so the port is free again. There is a
            // small race window where another process could grab the port,
            // but for a developer/CI machine this is acceptable.
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            try
            {
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        private static TcpListener StartListener(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();

            // Drain accepted sockets in the background. The subchannel
            // transport reaches ConnectivityState.Ready as soon as the
            // TCP handshake completes, so we do not need to do anything
            // with the accepted sockets beyond holding them open.
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var socket = await listener.AcceptSocketAsync();

                        // Keep the socket reference alive until the listener stops;
                        // GC-eligible immediately is fine because the TCP connection
                        // is established at this point and the subchannel has
                        // already transitioned to Ready.
                        _ = socket;
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Expected when the listener stops.
                }
                catch (SocketException)
                {
                    // Expected on listener shutdown.
                }
            });

            return listener;
        }

        private static GrpcChannel CreateChannel(int port)
        {
            // Match the production OutboundGrpcClient.CreateHttpHandler shape
            // closely so the test exercises the same transport configuration:
            // SocketsHttpHandler with an explicit ConnectTimeout.
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(5)
            };

            return GrpcChannel.ForAddress(
                new Uri($"http://127.0.0.1:{port}"),
                new GrpcChannelOptions
                {
                    HttpHandler = handler
                });
        }

        /// <summary>
        /// Validates the production wiring end-to-end on Linux: a channel
        /// built via <see cref="OutboundGrpcClient.CreateGrpcChannelOptions"/>
        /// should react to a listener that becomes available shortly after
        /// the first attempt in well under the original ~985&#x202F;ms (default
        /// 1&#x202F;s backoff) baseline. On Linux (production) expect
        /// ~50-200&#x202F;ms with the new 25&#x202F;ms backoff. On Windows the
        /// OS-level SYN retransmit dominates (~500&#x202F;ms) regardless of gRPC
        /// backoff — the upper bound is set to 700&#x202F;ms so this still passes
        /// on Windows while remaining well below the original 1&#x202F;s baseline.
        /// </summary>
        [Fact]
        public async Task ProductionChannelOptions_ConnectsFast_WhenListenerComesUpShortlyAfterFirstAttempt()
        {
            int port = GetFreeLoopbackPort();

            using var channel = GrpcChannel.ForAddress(
                new Uri($"http://127.0.0.1:{port}"),
                OutboundGrpcClient.CreateGrpcChannelOptions());

            TcpListener? listener = null;
            try
            {
                var listenerTask = Task.Run(async () =>
                {
                    await Task.Delay(50);
                    listener = StartListener(port);
                });

                using var cts = new CancellationTokenSource(TestBudget);
                var sw = Stopwatch.StartNew();
                await channel.ConnectAsync(cts.Token);
                sw.Stop();

                await listenerTask;

                _output.WriteLine(
                    $"Production CreateGrpcChannelOptions | Listener delay: 50 ms | ConnectAsync elapsed: {sw.ElapsedMilliseconds} ms");

                Assert.True(
                    sw.Elapsed < TimeSpan.FromMilliseconds(700),
                    $"Expected < 700 ms; actual {sw.ElapsedMilliseconds} ms. On Linux expect ~50-200 ms; on Windows the OS retransmit (~500 ms) dominates.");
            }
            finally
            {
                listener?.Stop();
            }
        }

        /// <summary>
        /// Sweep of listener-startup delays against the production
        /// <see cref="OutboundGrpcClient.CreateGrpcChannelOptions"/> wiring.
        /// Each case reports the actual elapsed time so the production
        /// retry cadence can be characterised without per-OS assertions.
        /// On Linux with the production constant 25 ms policy, expect
        /// connect time ≈ <c>listenerDelayMs</c> + ~25-30 ms (one retry
        /// quantum + scheduling). On Windows the OS TCP retransmit floor
        /// (~500 ms) dominates.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(100)]
        [InlineData(300)]
        [InlineData(600)]
        [InlineData(900)]
        [InlineData(1500)]
        public async Task Characterise_ProductionChannelOptions_VsListenerStartDelay(int listenerDelayMs)
        {
            int port = GetFreeLoopbackPort();

            using var channel = GrpcChannel.ForAddress(
                new Uri($"http://127.0.0.1:{port}"),
                OutboundGrpcClient.CreateGrpcChannelOptions());

            TcpListener? listener = null;
            try
            {
                var listenerTask = Task.Run(async () =>
                {
                    if (listenerDelayMs > 0)
                    {
                        await Task.Delay(listenerDelayMs);
                    }
                    listener = StartListener(port);
                });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var sw = Stopwatch.StartNew();
                await channel.ConnectAsync(cts.Token);
                sw.Stop();

                await listenerTask;

                _output.WriteLine(
                    $"Production CreateGrpcChannelOptions | Listener delay: {listenerDelayMs,5} ms | ConnectAsync elapsed: {sw.ElapsedMilliseconds,5} ms | Gap (connect - listener): {sw.ElapsedMilliseconds - listenerDelayMs,5} ms");
            }
            finally
            {
                listener?.Stop();
            }
        }

        /// <summary>
        /// Cross-platform behavioural characterisation of bare
        /// <see cref="Socket.ConnectAsync(System.Net.EndPoint, CancellationToken)"/>
        /// against a closed loopback port, then a listener that comes up
        /// after a configurable delay. On Linux the kernel returns
        /// <c>Connection refused</c> immediately on the first attempt (no
        /// SYN retransmit on loopback) — so this test catches that and
        /// retries until the listener is up, recording the elapsed time
        /// across attempts. On Windows the kernel performs internal SYN
        /// retries inside a single <c>ConnectAsync</c> call, so the first
        /// call typically succeeds. The recorded elapsed time is the
        /// OS-level cost a fresh-channel retry would have to pay.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(100)]
        [InlineData(300)]
        [InlineData(600)]
        [InlineData(1200)]
        public async Task Characterise_RawSocketConnect_VsListenerStartDelay(int listenerDelayMs)
        {
            int port = GetFreeLoopbackPort();

            TcpListener? listener = null;
            try
            {
                var listenerTask = Task.Run(async () =>
                {
                    if (listenerDelayMs > 0)
                    {
                        await Task.Delay(listenerDelayMs);
                    }
                    listener = StartListener(port);
                });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var sw = Stopwatch.StartNew();
                int attempt = 0;
                while (true)
                {
                    attempt++;
                    try
                    {
                        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cts.Token);
                        break;
                    }
                    catch (SocketException) when (!cts.IsCancellationRequested)
                    {
                        // Linux: immediate ECONNREFUSED. Brief delay then retry.
                        // This is the "fresh-channel-per-attempt" pattern that
                        // WorkerConnectionService's for-loop uses.
                        await Task.Delay(10, cts.Token);
                    }
                }
                sw.Stop();

                await listenerTask;

                _output.WriteLine(
                    $"Listener delay: {listenerDelayMs,5} ms | Raw socket.ConnectAsync elapsed: {sw.ElapsedMilliseconds,5} ms across {attempt,3} attempts | Gap (connect - listener): {sw.ElapsedMilliseconds - listenerDelayMs,5} ms");
            }
            finally
            {
                listener?.Stop();
            }
        }

        /// <summary>
        /// Captures every Subchannel log entry — in particular the
        /// <c>StartingConnectBackoff</c> entry — so we can see the exact
        /// backoff value gRPC chose at runtime. This confirms whether the
        /// observed ~500 ms cadence comes from the documented
        /// <c>ExponentialBackoffPolicy</c> or from some other source.
        /// </summary>
        [Fact]
        public async Task Characterise_ConnectAsync_CapturesActualBackoffFromGrpcLogs()
        {
            int port = GetFreeLoopbackPort();

            var capturedLogs = new List<string>();
            var loggerFactory = new CapturingLoggerFactory(capturedLogs);

            using var channel = GrpcChannel.ForAddress(
                new Uri($"http://127.0.0.1:{port}"),
                new GrpcChannelOptions
                {
                    HttpHandler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) },
                    LoggerFactory = loggerFactory
                });

            // Bring the listener up only after a sizeable delay so the gRPC
            // client logs at least two backoff cycles before connecting.
            TcpListener? listener = null;
            try
            {
                var listenerTask = Task.Run(async () =>
                {
                    await Task.Delay(1200);
                    listener = StartListener(port);
                });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var sw = Stopwatch.StartNew();
                await channel.ConnectAsync(cts.Token);
                sw.Stop();

                await listenerTask;

                _output.WriteLine($"ConnectAsync elapsed: {sw.ElapsedMilliseconds} ms");
                _output.WriteLine($"Captured {capturedLogs.Count} gRPC log entries:");
                foreach (string log in capturedLogs)
                {
                    _output.WriteLine($"  {log}");
                }
            }
            finally
            {
                listener?.Stop();
            }
        }

        private sealed class CapturingLoggerFactory : ILoggerFactory
        {
            private readonly List<string> _sink;

            public CapturingLoggerFactory(List<string> sink)
            {
                _sink = sink;
            }

            public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _sink);

            public void AddProvider(ILoggerProvider provider)
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly List<string> _sink;

            public CapturingLogger(string category, List<string> sink)
            {
                _category = category;
                _sink = sink;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Trace;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                string message = formatter(state, exception);

                // Only capture log entries that include backoff or connect timing — keeps the
                // captured output focused on the question this test answers.
                if (message.Contains("ackoff", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("connect", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("state changed", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_sink)
                    {
                        _sink.Add($"[{logLevel,-5}] [{_category}] {message}");
                    }
                }
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new NullScope();

                public void Dispose()
                {
                }
            }
        }
    }
}
