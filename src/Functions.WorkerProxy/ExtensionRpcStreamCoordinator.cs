// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Coordinates the active host extension RPC stream and worker-originated logical calls.
/// </summary>
internal sealed class ExtensionRpcStreamCoordinator
{
    internal const uint ProtocolVersion = 1;
    internal const uint DefaultMaxChunkSize = 64 * 1024;
    internal const ulong DefaultInitialWindowSize = 1024 * 1024;
    internal const ulong DefaultMaxMessageSize = 16 * 1024 * 1024;

    private readonly Lock _syncLock = new();
    private TaskCompletionSource _availabilityChanged = CreateChangedSource();
    private ExtensionRpcStream? _stream;
    private string? _sessionId;
    private long _nextCallId;
    private long _nextStreamId;

    /// <summary>
    /// Gets a value indicating whether a host extension RPC stream is currently connected.
    /// </summary>
    public bool HasConnectedStream => Volatile.Read(ref _stream) is not null;

    /// <summary>
    /// Registers the physical extension RPC stream used to relay logical calls.
    /// </summary>
    /// <param name="cancellationToken">A token that is cancelled when the physical stream ends.</param>
    /// <returns>A lease that unregisters and closes the stream when disposed.</returns>
    public ExtensionRpcStreamLease Open(CancellationToken cancellationToken)
    {
        ExtensionRpcStream stream;
        lock (_syncLock)
        {
            if (_stream is not null)
            {
                throw new InvalidOperationException("An extension RPC stream is already connected.");
            }

            _sessionId ??= Guid.NewGuid().ToString("N");
            string streamId = Interlocked.Increment(ref _nextStreamId).ToStringInvariant();
            stream = new ExtensionRpcStream(this, _sessionId, streamId, cancellationToken);
            _stream = stream;
            SignalAvailabilityChangedUnsynchronized();
        }

        stream.QueueHello();

        return new ExtensionRpcStreamLease(this, stream);
    }

    /// <summary>
    /// Opens a logical extension call on the connected and negotiated stream.
    /// </summary>
    /// <param name="start">The start message describing the worker-facing gRPC request.</param>
    /// <param name="cancellationToken">A token that cancels opening the call.</param>
    /// <returns>The opened extension call.</returns>
    public async Task<ExtensionCall> OpenExtensionCallAsync(
        ExtensionRpcStart start,
        CancellationToken cancellationToken)
    {
        TimeSpan? timeout = start.Timeout?.ToTimeSpan();
        long waitStart = Stopwatch.GetTimestamp();
        while (true)
        {
            ExtensionRpcStream? stream;
            Task? availabilityTask = null;
            lock (_syncLock)
            {
                stream = _stream ?? throw new InvalidOperationException("No extension RPC stream is connected.");
                if (!stream.IsReady)
                {
                    if (stream.IsNegotiated)
                    {
                        throw new InvalidOperationException("The host disabled the extension RPC stream.");
                    }

                    availabilityTask = _availabilityChanged.Task;
                    stream = null;
                }
            }

            if (stream is not null)
            {
                if (timeout is not null)
                {
                    TimeSpan remaining = timeout.Value - Stopwatch.GetElapsedTime(waitStart);
                    if (remaining <= TimeSpan.Zero)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    start.Timeout = Duration.FromTimeSpan(remaining);
                    UpdateTimeoutMetadata(start, remaining);
                }

                string callId = Interlocked.Increment(ref _nextCallId).ToStringInvariant();
                try
                {
                    return await stream.OpenExtensionCallAsync(callId, start, cancellationToken);
                }
                catch (OperationCanceledException) when (
                    stream.CancellationToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                }
                catch (ChannelClosedException) when (!cancellationToken.IsCancellationRequested)
                {
                }

                continue;
            }

            await availabilityTask!.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Unregisters and closes the specified physical stream.
    /// </summary>
    /// <param name="stream">The stream to close.</param>
    internal void Close(ExtensionRpcStream stream)
    {
        lock (_syncLock)
        {
            if (ReferenceEquals(_stream, stream))
            {
                _stream = null;
                _sessionId = null;
            }

            SignalAvailabilityChangedUnsynchronized();
        }

        stream.Close();
    }

    /// <summary>
    /// Signals callers waiting for the stream negotiation state to change.
    /// </summary>
    internal void SignalAvailabilityChanged()
    {
        lock (_syncLock)
        {
            SignalAvailabilityChangedUnsynchronized();
        }
    }

    private static void UpdateTimeoutMetadata(ExtensionRpcStart start, TimeSpan timeout)
    {
        string value = FormatTimeout(timeout);
        ExtensionRpcMetadataEntry? timeoutEntry = start.Metadata.FirstOrDefault(
            entry => string.Equals(entry.Key, "grpc-timeout", StringComparison.OrdinalIgnoreCase));
        if (timeoutEntry is null)
        {
            start.Metadata.Add(
                new ExtensionRpcMetadataEntry
                {
                    Key = "grpc-timeout",
                    Value = ByteString.CopyFromUtf8(value),
                });
        }
        else
        {
            timeoutEntry.Value = ByteString.CopyFromUtf8(value);
        }
    }

    private static string FormatTimeout(TimeSpan timeout)
    {
        long ticks = Math.Max(1, timeout.Ticks);
        if (ticks <= 999_999)
        {
            return $"{(ticks * 100).ToStringInvariant()}n";
        }

        (long Divisor, char Unit)[] units =
        [
            (TimeSpan.TicksPerMicrosecond, 'u'),
            (TimeSpan.TicksPerMillisecond, 'm'),
            (TimeSpan.TicksPerSecond, 'S'),
            (TimeSpan.TicksPerMinute, 'M'),
            (TimeSpan.TicksPerHour, 'H'),
        ];

        foreach ((long divisor, char unit) in units)
        {
            long value = (ticks + divisor - 1) / divisor;
            if (value <= 99_999_999)
            {
                return $"{value.ToStringInvariant()}{unit}";
            }
        }

        throw new InvalidOperationException("The extension gRPC timeout exceeds the protocol limit.");
    }

    private void SignalAvailabilityChangedUnsynchronized()
    {
        TaskCompletionSource changed = _availabilityChanged;
        _availabilityChanged = CreateChangedSource();
        changed.TrySetResult();
    }

    private static TaskCompletionSource CreateChangedSource()
    {
        return new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
