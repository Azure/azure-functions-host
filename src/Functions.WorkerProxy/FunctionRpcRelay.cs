// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Owns the active pair of runtime-facing and worker-facing FunctionRpc streams.
/// </summary>
/// <remarks>
/// A relay session admits one stream per <see cref="FunctionRpcRelaySide"/>. The first peer close,
/// cancellation, stream failure, or application shutdown terminates the whole session. A replacement
/// session is created only after both attachments from the previous session have released.
/// </remarks>
internal sealed partial class FunctionRpcRelay : IAsyncDisposable, IHostedLifecycleService
{
    private readonly Lock _syncLock = new();
    private readonly ILogger<FunctionRpcRelay> _logger;
    private FunctionRpcRelaySession? _currentSession;
    private FunctionRpcRelayTerminalState? _lastTerminalState;
    private long _nextSessionId;
    private bool _stopping;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionRpcRelay"/> class.
    /// </summary>
    /// <param name="logger">The logger used for terminal and secondary stream failures.</param>
    public FunctionRpcRelay(ILogger<FunctionRpcRelay> logger)
    {
        _logger = logger;
    }

    private enum FunctionRpcRelayAttachResult
    {
        Attached,
        Duplicate,
        Terminated
    }

    /// <summary>
    /// Gets the terminal state of the most recently completed relay session.
    /// </summary>
    internal FunctionRpcRelayTerminalState? LastTerminalState
    {
        get
        {
            lock (_syncLock)
            {
                return _lastTerminalState;
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Determines whether the current session owns a stream for the specified side.
    /// </summary>
    /// <param name="side">The stream side to inspect.</param>
    /// <returns><see langword="true"/> when the side is attached; otherwise, <see langword="false"/>.</returns>
    internal bool IsAttached(FunctionRpcRelaySide side)
    {
        lock (_syncLock)
        {
            return _currentSession?.IsAttached(side) == true;
        }
    }

    /// <summary>
    /// Attaches one gRPC duplex stream to the current relay session.
    /// </summary>
    /// <param name="side">The role owned by the listener accepting the stream.</param>
    /// <param name="requestStream">The messages produced by the attached peer.</param>
    /// <param name="responseStream">The messages relayed from the opposite peer.</param>
    /// <param name="cancellationToken">The gRPC call cancellation token.</param>
    /// <returns>A task that completes after the session terminates and this attachment releases.</returns>
    /// <exception cref="FunctionRpcRelayAttachmentException">
    /// The side is already attached, the previous session is still tearing down, or shutdown has started.
    /// </exception>
    public Task AttachAsync(FunctionRpcRelaySide side, IAsyncStreamReader<StreamingMessage> requestStream,
        IServerStreamWriter<StreamingMessage> responseStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestStream);
        ArgumentNullException.ThrowIfNull(responseStream);

        FunctionRpcRelaySession session;

        lock (_syncLock)
        {
            if (_stopping)
            {
                throw new FunctionRpcRelayAttachmentException(side, FunctionRpcRelayAttachmentFailure.Shutdown,
                    "The FunctionRpc relay is shutting down.");
            }

            if (_currentSession?.IsTerminalAndReleased == true)
            {
                ClearCurrentSessionLocked();
            }

            session = _currentSession ??= new FunctionRpcRelaySession(Interlocked.Increment(ref _nextSessionId), _logger);

            FunctionRpcRelayAttachResult attachResult = session.TryAttach(side);
            if (attachResult != FunctionRpcRelayAttachResult.Attached)
            {
                FunctionRpcRelayAttachmentFailure failure = attachResult switch
                {
                    FunctionRpcRelayAttachResult.Duplicate => FunctionRpcRelayAttachmentFailure.Duplicate,
                    FunctionRpcRelayAttachResult.Terminated =>
                        FunctionRpcRelayAttachmentFailure.PreviousSessionTearingDown,
                    _ => throw new InvalidOperationException($"Unexpected attach result '{attachResult}'.")
                };
                string message = failure == FunctionRpcRelayAttachmentFailure.Duplicate
                    ? $"A {side} FunctionRpc stream is already attached."
                    : "The previous FunctionRpc relay session is still tearing down.";

                throw new FunctionRpcRelayAttachmentException(side, failure, message);
            }
        }

        return RunAttachmentAsync(session, side, requestStream, responseStream, cancellationToken);
    }

    /// <summary>
    /// Prevents new attachments and terminates the active relay session.
    /// </summary>
    /// <param name="cancellationToken">A token that bounds the wait for all attachments to release.</param>
    /// <returns>A task that completes when the active session has been cleared.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        FunctionRpcRelaySession? session;
        lock (_syncLock)
        {
            _stopping = true;
            session = _currentSession;
        }

        if (session is null)
        {
            return;
        }

        session.RequestShutdown();
        await session.Released.WaitAsync(cancellationToken);
        TryClearReleasedSession(session);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }

    private async Task RunAttachmentAsync(FunctionRpcRelaySession session, FunctionRpcRelaySide side,
        IAsyncStreamReader<StreamingMessage> requestStream, IServerStreamWriter<StreamingMessage> responseStream, CancellationToken cancellationToken)
    {
        try
        {
            await session.RunAsync(side, requestStream, responseStream, cancellationToken);
        }
        finally
        {
            session.Detach(side);
            TryClearReleasedSession(session);
        }
    }

    private void TryClearReleasedSession(FunctionRpcRelaySession session)
    {
        lock (_syncLock)
        {
            if (ReferenceEquals(_currentSession, session) && session.IsTerminalAndReleased)
            {
                ClearCurrentSessionLocked();
            }
        }
    }

    private void ClearCurrentSessionLocked()
    {
        FunctionRpcRelaySession session = _currentSession ?? throw new InvalidOperationException("There is no current relay session to clear.");
        _lastTerminalState = session.Completion.GetAwaiter().GetResult();
        _currentSession = null;
    }
}
