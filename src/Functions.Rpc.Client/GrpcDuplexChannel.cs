// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Adapts an SDK <see cref="AsyncDuplexStreamingCall{TRequest, TResponse}"/> to a bidirectional
/// <see cref="Channel{T}"/>.
/// </summary>
/// <remarks>
/// Writes complete when a request is admitted to the outgoing queue, not when it reaches the peer.
/// Completing <see cref="Channel{T}.Writer"/> gracefully completes the gRPC request stream after queued requests drain.
/// The channel supports one response reader and multiple concurrent request writers.
/// </remarks>
/// <typeparam name="T">The message type used in both directions.</typeparam>
internal sealed class GrpcDuplexChannel<T> : Channel<T>, IAsyncDisposable
    where T : class
{
    private readonly AsyncDuplexStreamingCall<T, T> _call;
    private readonly Channel<T> _incoming;
    private readonly Channel<T> _outgoing;
    private readonly Task _readPump;
    private readonly CancellationTokenSource _shutdownSource = new();
    private readonly object _syncLock = new();
    private readonly Task _writePump;
    private Task _disposeTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDuplexChannel{T}"/> class.
    /// </summary>
    /// <param name="call">The SDK duplex call.</param>
    internal GrpcDuplexChannel(AsyncDuplexStreamingCall<T, T> call)
    {
        _call = call ?? throw new ArgumentNullException(nameof(call));

        _incoming = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = true,
        });
        _outgoing = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false,
        });
        Reader = _incoming.Reader;
        Writer = _outgoing.Writer;

        _readPump = PumpResponsesAsync();
        _writePump = PumpRequestsAsync();
    }

    /// <summary>
    /// Aborts the duplex call, stops both message pumps, and releases the call lifetime.
    /// Concurrent callers share the same cleanup operation.
    /// </summary>
    /// <returns>A task representing asynchronous cleanup.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_syncLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Exception cleanupException = null;
        _outgoing.Writer.TryComplete();

        try
        {
            await _shutdownSource.CancelAsync();
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        cleanupException = CaptureCleanupFailure(cleanupException, _call.Dispose);

        try
        {
            await Task.WhenAll(_readPump, _writePump);
        }
        catch (Exception exception)
        {
            cleanupException = CombineCleanupExceptions(cleanupException, exception);
        }

        cleanupException = CaptureCleanupFailure(cleanupException, _shutdownSource.Dispose);

        if (cleanupException is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupException).Throw();
        }
    }

    private async Task PumpResponsesAsync()
    {
        Exception error = null;

        try
        {
            while (await _call.ResponseStream.MoveNext(_shutdownSource.Token))
            {
                await _incoming.Writer.WriteAsync(_call.ResponseStream.Current, _shutdownSource.Token);
            }
        }
        catch (Exception) when (_shutdownSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            _outgoing.Writer.TryComplete(error);
            _incoming.Writer.TryComplete(error);

            if (error is not null)
            {
                _shutdownSource.Cancel();
            }
        }
    }

    private async Task PumpRequestsAsync()
    {
        Exception error = null;

        try
        {
            await foreach (T message in _outgoing.Reader.ReadAllAsync(_shutdownSource.Token))
            {
                await _call.RequestStream.WriteAsync(message);
            }

            await _call.RequestStream.CompleteAsync();
        }
        catch (Exception) when (_shutdownSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            if (error is not null)
            {
                _outgoing.Writer.TryComplete(error);
                _incoming.Writer.TryComplete(error);
                _shutdownSource.Cancel();
            }
        }
    }

    private static Exception CaptureCleanupFailure(Exception currentException, Action cleanup)
    {
        if (cleanup is null)
        {
            return currentException;
        }

        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            return CombineCleanupExceptions(currentException, exception);
        }

        return currentException;
    }

    private static Exception CombineCleanupExceptions(Exception currentException, Exception nextException)
        => currentException is null ? nextException : new AggregateException(currentException, nextException);
}
