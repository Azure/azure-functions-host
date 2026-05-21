// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Probes a destination's TCP port for readiness before the HTTP forwarding
/// middleware sends a request to it.
///
/// <para>
/// The .NET-isolated worker's <c>HttpUri</c> capability is registered eagerly:
/// the dynamic loopback port is picked (via a temp socket that is then released),
/// and the capability is announced via gRPC before Kestrel has actually called
/// <c>BindAsync</c> on that port. With the async pre-launcher and fast cold-start
/// path, the runtime can dispatch an invocation in the brief window between the
/// capability being announced and Kestrel binding, which surfaces as a
/// connection-refused (502) from YARP and a downstream 15 s
/// <c>FunctionStartTimeoutInSeconds</c> in the worker's coordinator (because the
/// matching HTTP context never arrives).
/// </para>
/// <para>
/// This probe closes the race by attempting a fast TCP connect (with a tight
/// retry loop) before each forward. Once a destination is observed ready it is
/// cached, so steady-state traffic incurs no extra syscalls.
/// </para>
/// <para>
/// AOT-safe: uses only <see cref="Socket"/>, <see cref="IPEndPoint"/>,
/// <see cref="Dns"/>, and <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// </para>
/// </summary>
internal sealed class WorkerEndpointReadinessProbe
{
    private readonly ConcurrentDictionary<string, byte> _readyDestinations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IPEndPoint> _endpointCache = new(StringComparer.Ordinal);
    private readonly ILogger<WorkerEndpointReadinessProbe> _logger;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _totalTimeout;

    public WorkerEndpointReadinessProbe(
        ILogger<WorkerEndpointReadinessProbe> logger,
        TimeSpan retryDelay,
        TimeSpan totalTimeout)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _retryDelay = retryDelay;
        _totalTimeout = totalTimeout;
    }

    /// <summary>
    /// Gets the number of milliseconds between probe attempts.
    /// </summary>
    public double RetryDelayMs => _retryDelay.TotalMilliseconds;

    /// <summary>
    /// Gets the total time the probe will keep retrying before giving up.
    /// </summary>
    public double TotalTimeoutMs => _totalTimeout.TotalMilliseconds;

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="destination"/> has been
    /// observed accepting TCP connections at least once in this process. Used by
    /// the middleware to skip the await on the steady-state hot path entirely.
    /// </summary>
    public bool IsKnownReady(string destination) => _readyDestinations.ContainsKey(destination);

    /// <summary>
    /// Awaits until the destination's TCP port accepts a connection, or until
    /// the configured total timeout elapses (or the supplied
    /// <paramref name="cancellationToken"/> fires). Returns <see langword="true"/>
    /// on success and caches the result so future calls return immediately.
    ///
    /// <para>
    /// The hot path (port already listening on the very first attempt) does
    /// exactly one <see cref="Socket"/> allocation and one
    /// <see cref="Socket.ConnectAsync(EndPoint, CancellationToken)"/> call: on
    /// loopback that completes in tens of microseconds. The single deadline
    /// <see cref="CancellationTokenSource"/> is allocated once outside the loop
    /// so retries don't pay a per-attempt allocation either.
    /// </para>
    ///
    /// <para>
    /// Returns <see langword="false"/> when the timeout elapses without a
    /// successful connect, or when the destination URI can't be parsed/resolved.
    /// Callers may still attempt to forward — they will get a 502 from YARP —
    /// but the warning we log here is the diagnostic signal that the worker
    /// never came online.
    /// </para>
    /// </summary>
    public async ValueTask<bool> WaitForReadyAsync(string destination, CancellationToken cancellationToken)
    {
        if (_readyDestinations.ContainsKey(destination))
        {
            return true;
        }

        if (!TryResolveEndpoint(destination, out var endpoint))
        {
            _logger.LogWarning(
                "[Readiness Probe] Destination could not be resolved; skipping probe. Destination={Destination}",
                destination);
            return false;
        }

        // A single deadline CTS bounds the whole loop AND every individual
        // ConnectAsync — so a hung connect (e.g., firewalled destination) still
        // unwinds in _totalTimeout. Allocated once outside the loop so the hot
        // path (port already listening) pays exactly one CTS allocation.
        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineCts.CancelAfter(_totalTimeout);
        var deadlineToken = deadlineCts.Token;

        var sw = Stopwatch.StartNew();
        int attempts = 0;
        Exception? lastError = null;

        while (true)
        {
            attempts++;

            try
            {
                using var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(endpoint, deadlineToken);

                _readyDestinations.TryAdd(destination, 0);
                _logger.LogInformation(
                    "[Readiness Probe] Destination ready. Destination={Destination}, ElapsedMs={ElapsedMs}, Attempts={Attempts}",
                    destination, sw.Elapsed.TotalMilliseconds, attempts);

                return true;
            }
            catch (SocketException ex)
            {
                // Expected during the race window: connection refused, host unreachable, etc.
                lastError = ex;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // Total-deadline elapsed during a connect attempt.
                break;
            }

            try
            {
                await Task.Delay(_retryDelay, deadlineToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogWarning(
            lastError,
            "[Readiness Probe] Destination not ready after {ElapsedMs} ms ({Attempts} attempts). Destination={Destination}. Forwarding will likely fail with 502.",
            sw.Elapsed.TotalMilliseconds, attempts, destination);

        return false;
    }

    private bool TryResolveEndpoint(string destination, out IPEndPoint endpoint)
    {
        if (_endpointCache.TryGetValue(destination, out var cached))
        {
            endpoint = cached;
            return true;
        }

        if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri))
        {
            endpoint = null!;
            return false;
        }

        IPAddress? address;
        if (IPAddress.TryParse(uri.Host, out var parsedIp))
        {
            address = parsedIp;
        }
        else if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            address = IPAddress.Loopback;
        }
        else
        {
            // Synchronous DNS — runs once per distinct destination. Real production
            // destinations are loopback, so this branch is exercised only by the
            // --worker-http-endpoint override (Aspire harness, integration tests).
            try
            {
                var addresses = Dns.GetHostAddresses(uri.Host);
                address = addresses.Length > 0 ? addresses[0] : null;
            }
            catch (SocketException)
            {
                endpoint = null!;
                return false;
            }
        }

        if (address is null)
        {
            endpoint = null!;
            return false;
        }

        endpoint = new IPEndPoint(address, uri.Port);
        _endpointCache.TryAdd(destination, endpoint);

        return true;
    }
}
