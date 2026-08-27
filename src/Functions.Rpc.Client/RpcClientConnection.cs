// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Owns one raw outbound FunctionRpc stream, including message queues, pumps, and network resources.
/// Message payloads remain uninterpreted at this transport layer.
/// </summary>
internal sealed partial class RpcClientConnection : IAsyncDisposable
{
    private readonly Task _completion;
    private readonly Channel<StreamingMessage> _duplexChannel;
    private readonly Channel<StreamingMessage> _inbound;
    private readonly ILogger<RpcClientConnection> _logger;
    private readonly Channel<StreamingMessage> _outbound;
    private readonly Task _readerTask;
    private readonly CancellationTokenSource _shutdownSource;
    private readonly TaskCompletionSource<TerminalState> _terminalSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Task _writerTask;
    private int _activeResponseReader;
    private TerminalState _terminalState;

    /// <summary>
    /// Initializes a new instance of the <see cref="RpcClientConnection"/> class.
    /// </summary>
    /// <param name="workerId">The worker identifier retained for later correlation.</param>
    /// <param name="duplexChannel">The duplex FunctionRpc channel owned by this connection.</param>
    /// <param name="logger">The logger used to report secondary cleanup failures.</param>
    internal RpcClientConnection(string workerId, Channel<StreamingMessage> duplexChannel,
        ILogger<RpcClientConnection> logger)
        : this(workerId, duplexChannel, logger, new CancellationTokenSource())
    {
    }

    internal RpcClientConnection(string workerId, Channel<StreamingMessage> duplexChannel,
        ILogger<RpcClientConnection> logger, CancellationTokenSource shutdownSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(duplexChannel);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(shutdownSource);

        WorkerId = workerId;
        _duplexChannel = duplexChannel;
        _logger = logger;
        _shutdownSource = shutdownSource;
        _outbound = Channel.CreateUnbounded<StreamingMessage>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false,
        });
        _inbound = Channel.CreateUnbounded<StreamingMessage>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = true,
        });

        _readerTask = ReadResponsesAsync();
        _writerTask = WriteRequestsAsync();
        _completion = SuperviseAsync();
    }

    /// <summary>
    /// Gets the worker identifier retained for correlation by later layers.
    /// The raw transport does not send a protocol handshake.
    /// </summary>
    internal string WorkerId { get; }

    /// <summary>
    /// Gets a task that completes after terminal state is selected and all owned resources are released.
    /// Transport failures remain observable through this task.
    /// </summary>
    /// <remarks>
    /// The unbounded response queue lets the response pump continue observing peer completion independently of consumers.
    /// </remarks>
    internal Task Completion => _completion;

    /// <summary>
    /// Adds an outbound message to the unbounded single-writer queue.
    /// Completion means queue admission, not delivery; disposal aborts queued messages.
    /// </summary>
    /// <param name="message">The uninterpreted FunctionRpc message to enqueue.</param>
    /// <param name="cancellationToken">A token that cancels only this enqueue operation.</param>
    /// <returns>A task that completes when the message is admitted to the queue.</returns>
    /// <remarks>
    /// The caller transfers ownership at invocation and must not mutate <paramref name="message"/> afterward.
    /// </remarks>
    internal async ValueTask EnqueueAsync(StreamingMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            await _outbound.Writer.WriteAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (GetTerminalState() is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ThrowTerminalOutcomeAsync();
        }
    }

    /// <summary>
    /// Asynchronously enumerates inbound messages from the unbounded response queue.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels this enumeration.</param>
    /// <returns>An asynchronous sequence of uninterpreted FunctionRpc responses.</returns>
    /// <remarks>
    /// Only one response enumeration may be active at a time. Clean peer closure ends enumeration after buffered responses
    /// are drained. Transport failure terminates enumeration with that failure. Explicit disposal while active aborts
    /// enumeration. Ending enumeration early does not close the connection; the owner must still call
    /// <see cref="DisposeAsync"/>.
    /// </remarks>
    internal async IAsyncEnumerable<StreamingMessage> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _activeResponseReader, 1, 0) != 0)
        {
            throw new InvalidOperationException("Only one response enumeration may be active at a time.");
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(GetTerminalState()?.Kind == TerminalKind.Disposed, this);

                StreamingMessage message;
                try
                {
                    message = await _inbound.Reader.ReadAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception) when (GetTerminalState() is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(GetTerminalState()?.Kind == TerminalKind.Disposed, this);
                    await _completion;
                    yield break;
                }

                yield return message;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _activeResponseReader, 0);
        }
    }

    /// <summary>
    /// Aborts the connection, waits for both pumps to stop, and releases the call and channel exactly once.
    /// A previously observed transport fault remains available through <see cref="Completion"/>.
    /// </summary>
    /// <returns>A task representing asynchronous shutdown.</returns>
    public async ValueTask DisposeAsync()
    {
        TryTerminate(TerminalState.CreateDisposed());

        try
        {
            await _completion;
        }
        catch (OperationCanceledException) when (GetTerminalState() is
        { Kind: TerminalKind.Disposed, CleanupException: null })
        {
        }
        catch (Exception) when (GetTerminalState()?.Kind == TerminalKind.Faulted)
        {
            TerminalState terminalState = GetTerminalState();
            if (terminalState.CleanupException is not null)
            {
                ExceptionDispatchInfo.Capture(terminalState.CleanupException).Throw();
            }
        }
    }

    /// <summary>
    /// Reads the terminal state with interlocked acquire semantics so concurrent readers observe the published outcome.
    /// </summary>
    /// <returns>The winning terminal state, or <see langword="null"/> while the connection is active.</returns>
    private TerminalState GetTerminalState() => Interlocked.CompareExchange(ref _terminalState, null, null);

    /// <summary>
    /// Copies responses from the gRPC response stream into the inbound queue.
    /// </summary>
    /// <returns>A task representing the response pump lifetime.</returns>
    private async Task ReadResponsesAsync()
    {
        try
        {
            await foreach (StreamingMessage message in _duplexChannel.Reader.ReadAllAsync(_shutdownSource.Token))
            {
                await _inbound.Writer.WriteAsync(message, _shutdownSource.Token);
            }

            TryTerminate(TerminalState.CreateCompleted());
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException exception) when (exception.InnerException is null)
        {
            TryTerminate(TerminalState.CreateCompleted());
        }
        catch (Exception exception)
        {
            TryTerminate(TerminalState.Faulted(exception));
        }
    }

    /// <summary>
    /// Serializes outbound queue messages onto the gRPC request stream.
    /// </summary>
    /// <returns>A task representing the sole request-stream writer lifetime.</returns>
    private async Task WriteRequestsAsync()
    {
        try
        {
            await foreach (StreamingMessage message in _outbound.Reader.ReadAllAsync(_shutdownSource.Token))
            {
                await _duplexChannel.Writer.WriteAsync(message, _shutdownSource.Token);
            }
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException exception) when (exception.InnerException is null)
        {
            // A clean response-stream close also closes the duplex channel's request boundary.
        }
        catch (Exception exception)
        {
            TryTerminate(TerminalState.Faulted(exception));
        }
    }

    /// <summary>
    /// Observes the first terminal outcome, stops both pumps, releases owned resources, and publishes final completion.
    /// </summary>
    /// <returns>A task representing aggregate connection completion.</returns>
    private async Task SuperviseAsync()
    {
        TerminalState terminalState = await _terminalSource.Task;
        Exception cleanupException = terminalState.CleanupException;

        // Disposing the channel unblocks both pumps.
        try
        {
            if (_duplexChannel is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
            else
            {
                _duplexChannel.Writer.TryComplete();
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "RPC client cleanup failed while disposing the duplex channel.");
            cleanupException = CombineCleanupExceptions(cleanupException, exception);
        }

        try
        {
            await Task.WhenAll(_readerTask, _writerTask);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "RPC client cleanup failed while stopping the message pumps.");
            cleanupException = CombineCleanupExceptions(cleanupException, exception);
        }

        CancellationToken shutdownToken = _shutdownSource.Token;
        cleanupException = CaptureCleanupFailure(cleanupException, _shutdownSource.Dispose, _logger, "dispose the connection lifetime");
        terminalState.CleanupException = cleanupException;

        if (terminalState.Exception is not null)
        {
            ExceptionDispatchInfo.Capture(terminalState.Exception).Throw();
        }

        if (cleanupException is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupException).Throw();
        }

        if (terminalState.Kind == TerminalKind.Disposed)
        {
            throw new OperationCanceledException("The RPC client connection was disposed.", shutdownToken);
        }
    }

    /// <summary>
    /// Translates a completed queue operation into the connection's established terminal outcome.
    /// </summary>
    /// <returns>A task that always completes by throwing the terminal outcome.</returns>
    private async Task ThrowTerminalOutcomeAsync()
    {
        ObjectDisposedException.ThrowIf(GetTerminalState()?.Kind == TerminalKind.Disposed, this);

        await _completion;
        throw new InvalidOperationException("The RPC client connection has completed.");
    }

    /// <summary>
    /// Runs one cleanup action, logs its failure, and retains any earlier cleanup failure.
    /// </summary>
    /// <param name="currentException">The cleanup failure already captured, if any.</param>
    /// <param name="cleanup">The next cleanup action to run.</param>
    /// <param name="logger">The logger used to report the cleanup failure.</param>
    /// <param name="operation">A description of the cleanup operation.</param>
    /// <returns>The accumulated cleanup failure, or <see langword="null"/> when cleanup remains successful.</returns>
    private static Exception CaptureCleanupFailure(Exception currentException, Action cleanup, ILogger logger, string operation)
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
            logger.LogWarning(exception, "RPC client cleanup failed while attempting to {CleanupOperation}.", operation);
            return CombineCleanupExceptions(currentException, exception);
        }

        return currentException;
    }

    /// <summary>
    /// Combines cleanup failures so later resource-release errors do not hide earlier failures.
    /// </summary>
    /// <param name="currentException">The cleanup failure already captured, if any.</param>
    /// <param name="nextException">The newly observed cleanup failure.</param>
    /// <returns>The combined cleanup failure.</returns>
    private static Exception CombineCleanupExceptions(Exception currentException, Exception nextException)
        => currentException is null ? nextException : new AggregateException(currentException, nextException);

    /// <summary>
    /// Atomically selects the first terminal outcome, cancels the pumps, and closes both queue boundaries.
    /// </summary>
    /// <param name="terminalState">The candidate terminal outcome.</param>
    private void TryTerminate(TerminalState terminalState)
    {
        // The first terminal transition owns the outcome and closes both queue boundaries.
        if (Interlocked.CompareExchange(ref _terminalState, terminalState, null) is not null)
        {
            return;
        }

        try
        {
            _shutdownSource.Cancel();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "RPC client cleanup failed while cancelling the message pumps.");
            terminalState.CleanupException = exception;
        }

        Exception completionException = terminalState.Kind switch
        {
            TerminalKind.Faulted => terminalState.Exception,
            TerminalKind.Disposed => new OperationCanceledException("The RPC client connection was disposed."),
            _ => null,
        };
        _outbound.Writer.TryComplete(completionException);
        _inbound.Writer.TryComplete(completionException);
        _terminalSource.TrySetResult(terminalState);
    }
}
