// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Owns one raw outbound FunctionRpc stream, including message queues, pumps, and network resources.
/// Message payloads remain uninterpreted at this transport layer.
/// </summary>
internal sealed class RpcClientConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets the maximum time allowed for an individual socket connection attempt.
    /// </summary>
    internal static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the idle interval before an HTTP/2 keepalive ping is sent.
    /// </summary>
    internal static readonly TimeSpan DefaultKeepAlivePingDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the time allowed for a keepalive ping acknowledgement.
    /// </summary>
    internal static readonly TimeSpan DefaultKeepAlivePingTimeout = TimeSpan.FromSeconds(10);

    private const int MaxMessageLengthBytes = int.MaxValue;

    private readonly AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> _call;
    private readonly IDisposable _channel;
    private readonly Task _completion;
    private readonly Channel<StreamingMessage> _inbound;
    private readonly ILogger<RpcClientConnection> _logger;
    private readonly Channel<StreamingMessage> _outbound;
    private readonly Task _readerTask;
    private readonly CancellationTokenSource _shutdownSource;
    private readonly TaskCompletionSource<TerminalState> _terminalSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Task _writerTask;
    private TerminalState _terminalState;

    /// <summary>
    /// Initializes a new instance of the <see cref="RpcClientConnection"/> class.
    /// The instance owns the established duplex call and its channel resources.
    /// </summary>
    /// <param name="options">The validated connection and queue options.</param>
    /// <param name="call">The duplex FunctionRpc call owned by this connection.</param>
    /// <param name="channel">The underlying channel resource owned by this connection.</param>
    /// <param name="shutdownSource">The cancellation source controlling the duplex call and both pumps.</param>
    /// <param name="logger">The logger used to report secondary cleanup failures.</param>
    internal RpcClientConnection(RpcClientConnectionOptions options,
        AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> call, IDisposable channel,
        CancellationTokenSource shutdownSource, ILogger<RpcClientConnection> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(shutdownSource);
        ArgumentNullException.ThrowIfNull(logger);

        WorkerId = options.WorkerId;
        _call = call;
        _channel = channel;
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

    private enum TerminalKind
    {
        Completed,
        Disposed,
        Faulted,
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
    /// Establishes a raw outbound FunctionRpc stream and transfers all network resource ownership to the returned
    /// connection.
    /// </summary>
    /// <param name="options">The validated connection and queue options.</param>
    /// <param name="logger">The logger used to report secondary cleanup failures.</param>
    /// <param name="cancellationToken">A token that bounds connection establishment only.</param>
    /// <returns>The fully initialized connection with both message pumps running.</returns>
    /// <remarks>
    /// Successful completion confirms channel connectivity and starts EventStream, but does not confirm service
    /// acceptance or perform the FunctionRpc handshake. A later rejection is reported through <see cref="Completion"/>.
    /// </remarks>
    internal static async Task<RpcClientConnection> ConnectAsync(RpcClientConnectionOptions options,
        ILogger<RpcClientConnection> logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        SocketsHttpHandler handler = CreateHttpHandler();
        GrpcChannel channel = null;
        AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> call = null;
        CancellationTokenSource shutdownSource = null;

        try
        {
            channel = GrpcChannel.ForAddress(options.Endpoint, new GrpcChannelOptions
            {
                DisposeHttpClient = true,
                HttpHandler = handler,
                MaxReceiveMessageSize = MaxMessageLengthBytes,
                MaxSendMessageSize = MaxMessageLengthBytes,
            });
            handler = null;

            // The caller token bounds establishment only; the connection owns the stream lifetime after this succeeds.
            await channel.ConnectAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            shutdownSource = new CancellationTokenSource();
            FunctionRpc.FunctionRpcClient client = new(channel);
            call = client.EventStream(cancellationToken: shutdownSource.Token);

            RpcClientConnection connection = new(options, call, channel, shutdownSource, logger);
            call = null;
            channel = null;
            shutdownSource = null;

            return connection;
        }
        catch
        {
            // Keep the connection failure primary while attempting every cleanup; log secondary failures for diagnosis.
            TryCleanup(() => call?.Dispose(), logger, "dispose the duplex call");
            TryCleanup(() => shutdownSource?.Cancel(), logger, "cancel the connection lifetime");
            TryCleanup(() => shutdownSource?.Dispose(), logger, "dispose the connection lifetime");
            TryCleanup(() => channel?.Dispose(), logger, "dispose the gRPC channel");
            TryCleanup(() => handler?.Dispose(), logger, "dispose the HTTP handler");

            throw;
        }
    }

    /// <summary>
    /// Creates the HTTP handler used by the gRPC channel with transport-level connection and keepalive settings.
    /// </summary>
    /// <returns>A handler whose ownership transfers to the gRPC channel.</returns>
    internal static SocketsHttpHandler CreateHttpHandler()
    {
        // Link-level retry policy remains above this raw transport; gRPC uses its default connection backoff here.
        return new SocketsHttpHandler
        {
            ConnectTimeout = DefaultConnectTimeout,
            KeepAlivePingDelay = DefaultKeepAlivePingDelay,
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            KeepAlivePingTimeout = DefaultKeepAlivePingTimeout,
        };
    }

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
    /// Clean peer closure ends enumeration after buffered responses are drained. Transport failure terminates
    /// enumeration with that failure. Disposal aborts enumeration and releases buffered responses. Ending enumeration
    /// early does not close the connection; the owner must still call <see cref="DisposeAsync"/>.
    /// </remarks>
    internal async IAsyncEnumerable<StreamingMessage> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
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
            while (await _call.ResponseStream.MoveNext(_shutdownSource.Token))
            {
                await _inbound.Writer.WriteAsync(_call.ResponseStream.Current, _shutdownSource.Token);
            }

            TryTerminate(TerminalState.CreateCompleted());
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
        {
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
                await _call.RequestStream.WriteAsync(message);
            }
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
        {
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

        // Disposing the call unblocks both pumps; the channel is released after they observe shutdown.
        try
        {
            _call.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "RPC client cleanup failed while disposing the duplex call.");
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

        ReleaseAbortedMessages(terminalState.Kind);

        try
        {
            _channel.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "RPC client cleanup failed while disposing the gRPC channel.");
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
    /// Runs one best-effort cleanup action and logs its failure without replacing the primary failure.
    /// </summary>
    /// <param name="cleanup">The cleanup action to run.</param>
    /// <param name="logger">The logger used to report the cleanup failure.</param>
    /// <param name="operation">A description of the cleanup operation.</param>
    private static void TryCleanup(Action cleanup, ILogger logger, string operation)
    {
        if (cleanup is null)
        {
            return;
        }

        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "RPC client cleanup failed while attempting to {CleanupOperation}.", operation);
        }
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
    /// Releases queued messages that can no longer be observed after terminal shutdown.
    /// </summary>
    /// <param name="terminalKind">The reason the connection terminated.</param>
    private void ReleaseAbortedMessages(TerminalKind terminalKind)
    {
        while (_outbound.Reader.TryRead(out _))
        {
        }

        if (terminalKind == TerminalKind.Disposed)
        {
            while (_inbound.Reader.TryRead(out _))
            {
            }
        }
    }

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

    /// <summary>
    /// Carries the winning terminal outcome and any failure encountered while releasing connection resources.
    /// </summary>
    private sealed class TerminalState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TerminalState"/> class.
        /// </summary>
        /// <param name="kind">The reason the connection terminated.</param>
        /// <param name="exception">The transport failure, when termination was faulted.</param>
        private TerminalState(TerminalKind kind, Exception exception = null)
        {
            Kind = kind;
            Exception = exception;
        }

        /// <summary>
        /// Gets the reason the connection terminated.
        /// </summary>
        internal TerminalKind Kind { get; }

        /// <summary>
        /// Gets the transport failure that caused termination, if any.
        /// </summary>
        internal Exception Exception { get; }

        /// <summary>
        /// Gets or sets a failure encountered while cancelling or releasing owned resources.
        /// </summary>
        internal Exception CleanupException { get; set; }

        /// <summary>
        /// Creates a clean peer-completion outcome.
        /// </summary>
        /// <returns>A new clean terminal state.</returns>
        internal static TerminalState CreateCompleted() => new(TerminalKind.Completed);

        /// <summary>
        /// Creates an explicit-disposal outcome.
        /// </summary>
        /// <returns>A new disposed terminal state.</returns>
        internal static TerminalState CreateDisposed() => new(TerminalKind.Disposed);

        /// <summary>
        /// Creates a faulted outcome while distinguishing unexpected transport cancellation from caller cancellation.
        /// </summary>
        /// <param name="exception">The failure reported by a message pump.</param>
        /// <returns>A new faulted terminal state.</returns>
        internal static TerminalState Faulted(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            Exception terminalException = exception is OperationCanceledException
                ? new InvalidOperationException("The RPC client transport was canceled unexpectedly.", exception)
                : exception;
            return new TerminalState(TerminalKind.Faulted, terminalException);
        }
    }
}
