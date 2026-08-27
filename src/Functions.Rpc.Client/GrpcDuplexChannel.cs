// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Adapts an SDK <see cref="AsyncDuplexStreamingCall{TRequest, TResponse}"/> to a bidirectional
/// <see cref="Channel{T}"/>.
/// </summary>
/// <typeparam name="T">The message type used in both directions.</typeparam>
internal sealed class GrpcDuplexChannel<T> : Channel<T>, IAsyncDisposable
    where T : class
{
    private readonly AsyncDuplexStreamingCall<T, T> _call;
    private readonly CancellationTokenSource _callLifetimeSource;
    private readonly Channel<T> _incoming;
    private readonly ILogger _logger;
    private readonly IDisposable _ownedResource;
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
    /// <param name="callLifetimeSource">The cancellation source used to create <paramref name="call"/>.</param>
    /// <param name="ownedResource">The underlying connection resource.</param>
    /// <param name="logger">The logger used for secondary cleanup failures.</param>
    internal GrpcDuplexChannel(AsyncDuplexStreamingCall<T, T> call, CancellationTokenSource callLifetimeSource,
        IDisposable ownedResource, ILogger logger)
    {
        _call = call ?? throw new ArgumentNullException(nameof(call));
        _callLifetimeSource = callLifetimeSource ?? throw new ArgumentNullException(nameof(callLifetimeSource));
        _ownedResource = ownedResource ?? throw new ArgumentNullException(nameof(ownedResource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _incoming = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
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
    /// Aborts the duplex call, stops both message pumps, and releases the call and its owned connection resource.
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
            _logger.LogWarning(exception, "gRPC duplex channel cleanup failed while stopping the message pumps.");
            cleanupException = exception;
        }

        try
        {
            await _callLifetimeSource.CancelAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "gRPC duplex channel cleanup failed while cancelling the call lifetime.");
            cleanupException = CombineCleanupExceptions(cleanupException, exception);
        }

        try
        {
            await Task.WhenAll(_readPump, _writePump);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "gRPC duplex channel cleanup failed while stopping the message pumps.");
            cleanupException = CombineCleanupExceptions(cleanupException, exception);
        }

        cleanupException = CaptureCleanupFailure(cleanupException, _call.Dispose, "dispose the SDK duplex call");
        cleanupException = CaptureCleanupFailure(cleanupException, _ownedResource.Dispose, "dispose the connection resource");
        cleanupException = CaptureCleanupFailure(cleanupException, _callLifetimeSource.Dispose, "dispose the call lifetime");
        cleanupException = CaptureCleanupFailure(cleanupException, _shutdownSource.Dispose, "dispose the message-pump lifetime");

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
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
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
                TryCancelPumps();
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
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
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
                TryCancelPumps();
            }
        }
    }

    private Exception CaptureCleanupFailure(Exception currentException, Action cleanup, string operation)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "gRPC duplex channel cleanup failed while attempting to {CleanupOperation}.", operation);
            return CombineCleanupExceptions(currentException, exception);
        }

        return currentException;
    }

    private void TryCancelPumps()
    {
        try
        {
            _shutdownSource.Cancel();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "gRPC duplex channel cleanup failed while cancelling the message pumps.");
        }
    }

    private static Exception CombineCleanupExceptions(Exception currentException, Exception nextException)
        => currentException is null ? nextException : new AggregateException(currentException, nextException);
}
