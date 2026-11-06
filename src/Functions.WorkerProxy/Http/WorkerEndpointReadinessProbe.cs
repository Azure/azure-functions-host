// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
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
    /// Invalidates the cached readiness state for a destination.
    /// </summary>
    /// <param name="destination">The destination to invalidate.</param>
    public void Invalidate(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (_readyDestinations.TryRemove(destination, out _))
        {
            Log.DestinationInvalidated(_logger);
        }
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
            catch (SocketException)
            {
                LogFailure(WorkerEndpointReadinessResult.NameResolutionFailed);
                return WorkerEndpointReadinessResult.NameResolutionFailed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                LogFailure(GetFailureResult(lastError));
                return GetFailureResult(lastError);
            }
        }

        if (addresses.Length == 0)
        {
            LogFailure(WorkerEndpointReadinessResult.NameResolutionFailed);
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
                        using IDisposable? scope = BeginOutcomeScope("Ready", "Ready");
                        Log.DestinationReady(_logger, stopwatch.Elapsed.TotalMilliseconds, attempts);
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
                    LogFailure(GetFailureResult(lastError));
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
                LogFailure(GetFailureResult(lastError));
                return GetFailureResult(lastError);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        LogFailure(GetFailureResult(lastError));

        return GetFailureResult(lastError);

        IDisposable? BeginOutcomeScope(string outcome, string reason) => _logger.BeginScope(new WorkerSystemLogContext
        {
            Operation = "Http",
            Phase = "Readiness",
            Outcome = outcome,
            ReasonCode = reason,
            Attempts = attempts,
            ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
        });

        void LogFailure(WorkerEndpointReadinessResult result)
        {
            using IDisposable? scope = BeginOutcomeScope("NotReady", result.ToString());
            if (result is WorkerEndpointReadinessResult.NameResolutionFailed)
            {
                Log.DestinationResolutionFailed(_logger, stopwatch.Elapsed.TotalMilliseconds, attempts);
            }
            else
            {
                Log.DestinationNotReady(_logger, stopwatch.Elapsed.TotalMilliseconds, attempts);
            }
        }
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
        [LoggerMessage(0, LogLevel.Warning, "Worker HTTP destination could not be resolved after {ElapsedMilliseconds} ms and {Attempts} connection attempts.")]
        public static partial void DestinationResolutionFailed(ILogger logger, double elapsedMilliseconds, int attempts);

        [LoggerMessage(1, LogLevel.Information,
            "Worker HTTP destination became ready after {ElapsedMilliseconds} ms and {Attempts} attempts.")]
        public static partial void DestinationReady(ILogger logger, double elapsedMilliseconds, int attempts);

        [LoggerMessage(2, LogLevel.Warning,
            "Worker HTTP destination was not ready after {ElapsedMilliseconds} ms and {Attempts} attempts.")]
        public static partial void DestinationNotReady(
            ILogger logger,
            double elapsedMilliseconds,
            int attempts);

        [LoggerMessage(3, LogLevel.Debug, "Worker HTTP destination readiness was invalidated.")]
        public static partial void DestinationInvalidated(ILogger logger);
    }
}
