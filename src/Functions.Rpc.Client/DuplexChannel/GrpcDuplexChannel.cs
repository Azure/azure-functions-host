// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Adapts an SDK <see cref="AsyncDuplexStreamingCall{TRequest, TResponse}"/> to a bidirectional
/// <see cref="DuplexChannel{T}"/>.
/// </summary>
/// <remarks>
/// Writes complete when a request is admitted to the outgoing queue, not when it reaches the peer.
/// Completing <see cref="Channel{T}.Writer"/> gracefully completes the gRPC request stream after queued requests drain.
/// The channel supports one response reader and multiple concurrent request writers.
/// </remarks>
/// <typeparam name="T">The message type used in both directions.</typeparam>
internal sealed class GrpcDuplexChannel<T> : DuplexChannel<T>
    where T : class
{
    private readonly AsyncDuplexStreamingCall<T, T> _call;
    private readonly Channel<T> _incoming;
    private readonly Channel<T> _outgoing;
    private readonly Task _readPump;
    private readonly CancellationTokenSource _shutdownSource = new();
    private readonly Task _writePump;

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

    protected override async ValueTask DisposeAsyncCore()
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

        cleanupException = _call.DisposeAndCaptureException(cleanupException);

        try
        {
            await Task.WhenAll(_readPump, _writePump);
        }
        catch (Exception exception)
        {
            cleanupException = AggregateException.Combine(cleanupException, exception);
        }

        cleanupException = _shutdownSource.DisposeAndCaptureException(cleanupException);

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
                await _shutdownSource.CancelAsync();
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
                await _shutdownSource.CancelAsync();
            }
        }
    }
}
