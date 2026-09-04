// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Waits for worker HTTP destinations to accept TCP connections and caches successful probes.
/// </summary>
/// <remarks>
/// Workers can advertise a dynamically selected HTTP port before their HTTP server has finished
/// binding it. An invocation forwarded during that interval receives a connection refusal and can
/// eventually surface as a worker startup timeout. This probe closes that race by waiting for the
/// advertised port to accept TCP connections before forwarding the first request.
/// </remarks>
internal sealed partial class WorkerEndpointReadinessProbe(
    IOptions<WorkerEndpointReadinessProbeOptions> options,
    ILogger<WorkerEndpointReadinessProbe> logger)
{
    private readonly ConcurrentDictionary<Uri, byte> _readyDestinations = [];
    private readonly ILogger<WorkerEndpointReadinessProbe> _logger = logger;
    private readonly TimeSpan _retryDelay = options.Value.RetryDelay;
    private readonly TimeSpan _totalTimeout = options.Value.TotalTimeout;

    /// <summary>
    /// Determines whether a destination has already accepted a connection.
    /// </summary>
    /// <param name="destination">The destination to inspect.</param>
    /// <returns><see langword="true"/> when readiness is cached; otherwise, <see langword="false"/>.</returns>
    public bool IsKnownReady(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return _readyDestinations.ContainsKey(destination);
    }

    /// <summary>
    /// Waits for a destination to accept a TCP connection within the readiness deadline.
    /// </summary>
    /// <param name="destination">The destination to probe.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>The bounded readiness result.</returns>
    public async ValueTask<WorkerEndpointReadinessResult> WaitForReadyAsync(
        Uri destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (_readyDestinations.ContainsKey(destination))
        {
            return WorkerEndpointReadinessResult.Ready;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_totalTimeout);
        Stopwatch stopwatch = Stopwatch.StartNew();
        int attempts = 0;
        Exception? lastError = null;

        IPAddress[] addresses;
        if (string.Equals(destination.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            addresses = [IPAddress.Loopback, IPAddress.IPv6Loopback];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(destination.Host, deadline.Token);
            }
            catch (SocketException exception)
            {
                Log.DestinationResolutionFailed(_logger, exception, destination);
                return WorkerEndpointReadinessResult.NameResolutionFailed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                Log.DestinationNotReady(_logger, lastError, destination, stopwatch.Elapsed, attempts);
                return GetFailureResult(lastError);
            }
        }

        if (addresses.Length == 0)
        {
            Log.DestinationResolutionFailed(_logger, exception: null, destination);
            return WorkerEndpointReadinessResult.NameResolutionFailed;
        }

        while (!deadline.IsCancellationRequested)
        {
            foreach (IPAddress address in addresses)
            {
                attempts++;

                try
                {
                    using Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(new IPEndPoint(address, destination.Port), deadline.Token);
                    if (_readyDestinations.TryAdd(destination, 0))
                    {
                        Log.DestinationReady(_logger, destination, stopwatch.Elapsed.TotalMilliseconds, attempts);
                    }

                    return WorkerEndpointReadinessResult.Ready;
                }
                catch (SocketException exception)
                {
                    lastError = exception;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    Log.DestinationNotReady(_logger, lastError, destination, stopwatch.Elapsed, attempts);
                    return GetFailureResult(lastError);
                }
            }

            try
            {
                await Task.Delay(_retryDelay, deadline.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                Log.DestinationNotReady(_logger, lastError, destination, stopwatch.Elapsed, attempts);
                return GetFailureResult(lastError);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        Log.DestinationNotReady(_logger, lastError, destination, stopwatch.Elapsed, attempts);

        return GetFailureResult(lastError);
    }

    private static WorkerEndpointReadinessResult GetFailureResult(Exception? lastError)
    {
        return lastError switch
        {
            SocketException { SocketErrorCode: SocketError.ConnectionRefused } =>
                WorkerEndpointReadinessResult.ConnectionRefused,
            SocketException { SocketErrorCode: SocketError.TimedOut } =>
                WorkerEndpointReadinessResult.Timeout,
            SocketException => WorkerEndpointReadinessResult.ConnectionFailed,
            _ => WorkerEndpointReadinessResult.Timeout
        };
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "Worker HTTP destination {Destination} could not be resolved.")]
        public static partial void DestinationResolutionFailed(ILogger logger, Exception? exception, Uri destination);

        [LoggerMessage(1, LogLevel.Information,
            "Worker HTTP destination {Destination} became ready after {ElapsedMilliseconds} ms and {Attempts} attempts.")]
        public static partial void DestinationReady(ILogger logger, Uri destination, double elapsedMilliseconds, int attempts);

        public static void DestinationNotReady(
            ILogger logger,
            Exception? exception,
            Uri destination,
            TimeSpan elapsed,
            int attempts)
        {
            DestinationNotReady(logger, exception, destination, elapsed.TotalMilliseconds, attempts);
        }

        [LoggerMessage(2, LogLevel.Warning,
            "Worker HTTP destination {Destination} was not ready after {ElapsedMilliseconds} ms and {Attempts} attempts.")]
        private static partial void DestinationNotReady(
            ILogger logger,
            Exception? exception,
            Uri destination,
            double elapsedMilliseconds,
            int attempts);
    }
}
