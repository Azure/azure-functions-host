// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy;

internal sealed partial class FunctionRpcRelay
{
    /// <summary>
    /// Owns the queues, forwarding tasks, and terminal state for one runtime/worker stream pair.
    /// </summary>
    private sealed class FunctionRpcRelaySession(long id, ILogger logger)
    {
        private readonly Lock _stateLock = new();
        private readonly Channel<StreamingMessage> _toRuntime = CreateChannel();
        private readonly Channel<StreamingMessage> _toWorker = CreateChannel();
        // Linked call sources unregister when disposed, so cancellation can safely finish after this session is cleared.
        private readonly CancellationTokenSource _terminationSource = new();
        private readonly TaskCompletionSource<FunctionRpcRelayTerminalState> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly ILogger _logger = logger;
        private FunctionRpcRelayTerminalState? _terminalState;
        private bool _runtimeAttached;
        private bool _workerAttached;

        /// <summary>
        /// Gets the relay session identifier.
        /// </summary>
        public long Id { get; } = id;

        /// <summary>
        /// Gets the terminal state recorded for this session, or <see langword="null"/> if the session has not terminated.
        /// </summary>
        public FunctionRpcRelayTerminalState? TerminalState => _terminalState;

        /// <summary>
        /// Gets a task that completes after every accepted attachment has released.
        /// </summary>
        public Task Released => _released.Task;

        /// <summary>
        /// Gets a value indicating whether the terminal session has released both attachments and can be cleared.
        /// </summary>
        public bool IsTerminalAndReleased
        {
            get
            {
                lock (_stateLock)
                {
                    return _terminalState is not null && !HasAttachmentsLocked();
                }
            }
        }

        /// <summary>
        /// Atomically reserves ownership of one side for an attaching stream.
        /// </summary>
        /// <param name="side">The side to reserve.</param>
        /// <returns>The result of the reservation attempt.</returns>
        public FunctionRpcRelayAttachResult TryAttach(FunctionRpcRelaySide side)
        {
            lock (_stateLock)
            {
                if (_terminalState is not null)
                {
                    return FunctionRpcRelayAttachResult.Terminated;
                }

                if (IsAttachedLocked(side))
                {
                    return FunctionRpcRelayAttachResult.Duplicate;
                }

                SetAttachedLocked(side, value: true);
                return FunctionRpcRelayAttachResult.Attached;
            }
        }

        /// <summary>
        /// Determines whether this session currently owns the specified side.
        /// </summary>
        /// <param name="side">The side to inspect.</param>
        /// <returns><see langword="true"/> when the side is reserved; otherwise, <see langword="false"/>.</returns>
        public bool IsAttached(FunctionRpcRelaySide side)
        {
            lock (_stateLock)
            {
                return IsAttachedLocked(side);
            }
        }

        /// <summary>
        /// Runs the inbound reader and sole outbound writer for an attached gRPC stream.
        /// </summary>
        /// <param name="side">The attached stream side.</param>
        /// <param name="requestStream">The messages produced by this peer.</param>
        /// <param name="responseStream">The messages consumed by this peer.</param>
        /// <param name="cancellationToken">The gRPC call cancellation token.</param>
        /// <returns>A task that always terminates with the session's terminal state.</returns>
        /// <exception cref="FunctionRpcRelayTerminatedException">The relay session has terminated.</exception>
        public async Task RunAsync(FunctionRpcRelaySide side, IAsyncStreamReader<StreamingMessage> requestStream,
            IServerStreamWriter<StreamingMessage> responseStream, CancellationToken cancellationToken)
        {
            using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_terminationSource.Token, cancellationToken);

            ChannelWriter<StreamingMessage> destination = side switch
            {
                FunctionRpcRelaySide.Runtime => _toWorker.Writer,
                FunctionRpcRelaySide.Worker => _toRuntime.Writer,
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown relay side.")
            };
            ChannelReader<StreamingMessage> source = side switch
            {
                FunctionRpcRelaySide.Runtime => _toRuntime.Reader,
                FunctionRpcRelaySide.Worker => _toWorker.Reader,
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown relay side.")
            };

            Task readTask = ReadInboundAsync(requestStream, destination, linkedSource.Token);
            Task writeTask = WriteOutboundAsync(source, responseStream, linkedSource.Token);
            Task firstCompletedTask = await Task.WhenAny(readTask, writeTask);

            if (!_completion.Task.IsCompleted)
            {
                FunctionRpcRelayTerminalState terminalState = await ClassifyCompletionAsync(side, firstCompletedTask, cancellationToken);
                TryTerminate(terminalState);
            }

            await linkedSource.CancelAsync();
            // Reads can remain blocked until the transport releases them, so observe that task
            // out of band. The write must finish before gRPC writes trailers to the same response.
            _ = ObserveStreamOperationAfterTerminationAsync(side, "read", readTask);
            await ObserveStreamOperationAfterTerminationAsync(side, "write", writeTask);

            FunctionRpcRelayTerminalState finalState = await _completion.Task;
            throw new FunctionRpcRelayTerminatedException(finalState);
        }

        /// <summary>
        /// Releases ownership of one side and signals when the terminal session has no attachments.
        /// </summary>
        /// <param name="side">The side to release.</param>
        public void Detach(FunctionRpcRelaySide side)
        {
            lock (_stateLock)
            {
                if (!IsAttachedLocked(side))
                {
                    return;
                }

                SetAttachedLocked(side, value: false);
                SignalReleasedIfCompleteLocked();
            }
        }

        /// <summary>
        /// Records application shutdown as the terminal state for this session.
        /// </summary>
        public void RequestShutdown()
        {
            TryTerminate(new FunctionRpcRelayTerminalState(Id, FunctionRpcRelayTerminationReason.Shutdown, Side: null, Exception: null));
        }

        private static Channel<StreamingMessage> CreateChannel()
        {
            return Channel.CreateUnbounded<StreamingMessage>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = true
            });
        }

        private static async Task ReadInboundAsync(IAsyncStreamReader<StreamingMessage> requestStream,
            ChannelWriter<StreamingMessage> destination, CancellationToken cancellationToken)
        {
            while (await requestStream.MoveNext(cancellationToken))
            {
                await destination.WriteAsync(requestStream.Current, cancellationToken);
            }
        }

        private static async Task WriteOutboundAsync(ChannelReader<StreamingMessage> source,
            IServerStreamWriter<StreamingMessage> responseStream, CancellationToken cancellationToken)
        {
            await foreach (StreamingMessage message in source.ReadAllAsync(cancellationToken))
            {
                // This is the only task that writes this response stream. The linked token
                // releases an in-flight gRPC write during teardown.
                await responseStream.WriteAsync(message, cancellationToken);
            }
        }

        private async Task<FunctionRpcRelayTerminalState> ClassifyCompletionAsync(FunctionRpcRelaySide side,
            Task completedTask, CancellationToken callCancellationToken)
        {
            if (_completion.Task.IsCompleted)
            {
                return await _completion.Task;
            }

            try
            {
                await completedTask;

                return new FunctionRpcRelayTerminalState(Id, FunctionRpcRelayTerminationReason.PeerClosed, side, Exception: null);
            }
            catch (OperationCanceledException exception)
            {
                if (_completion.Task.IsCompleted)
                {
                    return await _completion.Task;
                }

                return new FunctionRpcRelayTerminalState(Id, FunctionRpcRelayTerminationReason.Canceled, side, exception);
            }
            catch (Exception exception) when (callCancellationToken.IsCancellationRequested)
            {
                return new FunctionRpcRelayTerminalState(Id, FunctionRpcRelayTerminationReason.Canceled, side, exception);
            }
            catch (Exception exception)
            {
                return new FunctionRpcRelayTerminalState(Id, FunctionRpcRelayTerminationReason.Faulted, side, exception);
            }
        }

        private bool TryTerminate(FunctionRpcRelayTerminalState terminalState)
        {
            lock (_stateLock)
            {
                if (_terminalState is not null)
                {
                    return false;
                }

                _terminalState = terminalState;
                _completion.SetResult(terminalState);
            }

            Exception? completionException = terminalState.Reason switch
            {
                FunctionRpcRelayTerminationReason.Faulted => terminalState.Exception,
                FunctionRpcRelayTerminationReason.Canceled => terminalState.Exception,
                _ => null
            };
            _toRuntime.Writer.TryComplete(completionException);
            _toWorker.Writer.TryComplete(completionException);

            if (terminalState.Reason == FunctionRpcRelayTerminationReason.Faulted)
            {
                _logger.LogWarning(terminalState.Exception, "FunctionRpc relay session {SessionId} faulted on the {Side} side.",
                    terminalState.SessionId, terminalState.Side);
            }
            else
            {
                _logger.LogDebug(terminalState.Exception, "FunctionRpc relay session {SessionId} terminated with {Reason} on the {Side} side.",
                    terminalState.SessionId, terminalState.Reason, terminalState.Side);
            }

            try
            {
                _terminationSource.Cancel();
            }
            finally
            {
                lock (_stateLock)
                {
                    SignalReleasedIfCompleteLocked();
                }
            }

            return true;
        }

        private async Task ObserveStreamOperationAfterTerminationAsync(FunctionRpcRelaySide side, string operationName, Task operationTask)
        {
            try
            {
                await operationTask;
            }
            catch (OperationCanceledException) when (_terminationSource.IsCancellationRequested)
            {
            }
            catch (ChannelClosedException) when (_completion.Task.IsCompleted)
            {
            }
            catch (Exception exception) when (_completion.Task.IsCompleted)
            {
                _logger.LogDebug(exception,
                    "FunctionRpc relay session {SessionId} observed a secondary {OperationName} stream failure on the {Side} side.",
                    Id, operationName, side);
            }
        }

        private bool HasAttachmentsLocked()
        {
            return _runtimeAttached || _workerAttached;
        }

        private bool IsAttachedLocked(FunctionRpcRelaySide side)
        {
            return side switch
            {
                FunctionRpcRelaySide.Runtime => _runtimeAttached,
                FunctionRpcRelaySide.Worker => _workerAttached,
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown relay side.")
            };
        }

        private void SetAttachedLocked(FunctionRpcRelaySide side, bool value)
        {
            switch (side)
            {
                case FunctionRpcRelaySide.Runtime:
                    _runtimeAttached = value;
                    break;
                case FunctionRpcRelaySide.Worker:
                    _workerAttached = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown relay side.");
            }
        }

        private void SignalReleasedIfCompleteLocked()
        {
            if (_terminalState is not null && !HasAttachmentsLocked())
            {
                _released.TrySetResult(true);
            }
        }
    }
}
