// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Identity;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;
using Microsoft.Azure.WebJobs.Script.Grpc.Transport;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Rpc;

internal sealed partial class FunctionRpcRelay
{
    /// <summary>
    /// Owns the queues, forwarding tasks, and terminal state for one runtime/worker stream pair.
    /// </summary>
    private sealed class FunctionRpcRelaySession(
        long id,
        ILogger logger,
        IWorkerCapabilityFinalizer capabilityFinalizer,
        WorkerPodStateManager stateManager,
        TokenServiceProxyService? tokenServiceProxy)
    {
        private const string FunctionGroupNameCapability = "FunctionGroupName";
        private readonly Lock _stateLock = new();
        private readonly Channel<StreamingMessage> _toRuntime = CreateChannel(singleWriter: false);
        private readonly Channel<StreamingMessage> _toWorker = CreateChannel();
        // Linked call sources unregister when disposed, so cancellation can safely finish after this session is cleared.
        private readonly CancellationTokenSource _terminationSource = new();
        private readonly TaskCompletionSource<FunctionRpcRelayTerminalState> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<StreamingMessage> _startStream = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private FunctionRpcRelayTerminalState? _terminalState;
        private FrozenDictionary<string, string>? _capabilities;
        private Uri? _workerHttpDestination;
        private bool _runtimeAttached;
        private bool _workerAttached;
        private long _runtimeGeneration;
        private bool _runtimeProtocolStarted;

        /// <summary>
        /// Gets the terminal state recorded for this session, or <see langword="null"/> if the session has not terminated.
        /// </summary>
        public FunctionRpcRelayTerminalState? TerminalState => _terminalState;

        /// <summary>
        /// Gets the initialized destination while this session remains live.
        /// </summary>
        public Uri? WorkerHttpDestination
        {
            get
            {
                lock (_stateLock)
                {
                    return _terminalState is null ? _workerHttpDestination : null;
                }
            }
        }

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
                if (side is FunctionRpcRelaySide.Runtime)
                {
                    _runtimeGeneration = checked(_runtimeGeneration + 1);
                }
                if (side == FunctionRpcRelaySide.Worker)
                {
                    // Observe admission without changing relay replacement policy; a failed assignment stays terminal.
                    stateManager.OnWorkerAttached(id);
                }

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
        /// <returns>A task that returns the first terminal state recorded for the session.</returns>
        public async Task<FunctionRpcRelayTerminalState> RunAsync(FunctionRpcRelaySide side, IAsyncStreamReader<StreamingMessage> requestStream,
            IServerStreamWriter<StreamingMessage> responseStream, CancellationToken cancellationToken)
        {
            using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_terminationSource.Token, cancellationToken);
            long runtimeGeneration;
            lock (_stateLock)
            {
                runtimeGeneration = _runtimeGeneration;
            }
            TrackedMessageWriter<StreamingMessage>? runtimeWrites = side is FunctionRpcRelaySide.Runtime ? new() : null;
            using TokenServiceProxyService.Connection? proxyConnection = AttachTokenServiceProxy(side, runtimeWrites, linkedSource.Token);

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

            Task readTask = ReadInboundAsync(side, requestStream, destination, runtimeGeneration, proxyConnection, linkedSource.Token);
            Task writeTask = WriteOutboundAsync(side, source, responseStream, runtimeWrites, linkedSource.Token);
            Task firstCompletedTask = await Task.WhenAny(readTask, writeTask);
            FunctionRpcRelayTerminalState? detachedRuntime = null;

            if (!_completion.Task.IsCompleted)
            {
                FunctionRpcRelayTerminalState terminalState = await ClassifyCompletionAsync(side, firstCompletedTask, cancellationToken);
                if (side is FunctionRpcRelaySide.Runtime && TryDetachBeforeInitialization(runtimeGeneration))
                {
                    detachedRuntime = terminalState;
                    Log.RuntimeDetachedBeforeInitialization(logger, id, terminalState.Reason);
                }
                else
                {
                    TryTerminate(terminalState);
                }
            }

            await linkedSource.CancelAsync();
            // Reads can remain blocked until the transport releases them, so observe that task
            // out of band. The write must finish before gRPC writes trailers to the same response.
            _ = ObserveStreamOperationAfterTerminationAsync(side, "read", readTask, linkedSource.Token);
            await ObserveStreamOperationAfterTerminationAsync(side, "write", writeTask, linkedSource.Token);

            return detachedRuntime ?? await _completion.Task;
        }

        private bool TryDetachBeforeInitialization(long runtimeGeneration)
        {
            lock (_stateLock)
            {
                if (_terminalState is not null || !_workerAttached || _runtimeProtocolStarted
                    || runtimeGeneration != _runtimeGeneration)
                {
                    return false;
                }

                // Fence a canceled reader that ignores transport cancellation before another runtime can attach.
                _runtimeGeneration = checked(_runtimeGeneration + 1);

                return true;
            }
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
                if (side == FunctionRpcRelaySide.Worker)
                {
                    stateManager.OnSessionTerminated(id);
                }

                SignalReleasedIfCompleteLocked();
            }
        }

        /// <summary>
        /// Records application shutdown as the terminal state for this session.
        /// </summary>
        public void RequestShutdown()
        {
            TryTerminate(new FunctionRpcRelayTerminalState(id, FunctionRpcRelayTerminationReason.Shutdown, Side: null, Exception: null));
        }

        private static Channel<StreamingMessage> CreateChannel(bool singleWriter = true)
        {
            return Channel.CreateUnbounded<StreamingMessage>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = singleWriter
            });
        }

        /// <summary>
        /// Makes only the Host-facing stream available to the identity bridge, before the customer stream attaches.
        /// </summary>
        private TokenServiceProxyService.Connection? AttachTokenServiceProxy(
            FunctionRpcRelaySide side, TrackedMessageWriter<StreamingMessage>? runtimeWrites, CancellationToken cancellationToken)
        {
            if (side is not FunctionRpcRelaySide.Runtime || tokenServiceProxy is not { Enabled: true })
            {
                return null;
            }

            return tokenServiceProxy.Attach((message, token, prepare) =>
                runtimeWrites!.WriteAsync(_toRuntime.Writer, message, token, prepare), cancellationToken);
        }

        private async Task ReadInboundAsync(FunctionRpcRelaySide side, IAsyncStreamReader<StreamingMessage> requestStream,
            ChannelWriter<StreamingMessage> destination, long runtimeGeneration,
            TokenServiceProxyService.Connection? proxyConnection, CancellationToken cancellationToken)
        {
            bool isFirstMessage = true;
            while (await requestStream.MoveNext(cancellationToken))
            {
                StreamingMessage current = requestStream.Current;
                if (current.HttpProxy is { } control)
                {
                    if (side is FunctionRpcRelaySide.Runtime)
                    {
                        lock (_stateLock)
                        {
                            if (_terminalState is not null || runtimeGeneration != _runtimeGeneration || !_runtimeAttached)
                            {
                                return;
                            }
                        }
                    }
                    if (side is not FunctionRpcRelaySide.Runtime || proxyConnection is null)
                    {
                        throw new InvalidDataException("HTTP proxy controls are only accepted from an enabled runtime transport.");
                    }

                    await proxyConnection.ReceiveAsync(control);
                    continue;
                }

                StreamingMessage? message = ProcessInboundMessage(side, current, isFirstMessage, runtimeGeneration);
                if (message is null)
                {
                    return;
                }

                bool startStream = side is FunctionRpcRelaySide.Worker && isFirstMessage;
                isFirstMessage = false;
                if (startStream)
                {
                    continue;
                }
                await destination.WriteAsync(message, cancellationToken);
            }
        }

        private StreamingMessage? ProcessInboundMessage(FunctionRpcRelaySide side, StreamingMessage message, bool isFirstMessage,
            long runtimeGeneration)
        {
            if (side != FunctionRpcRelaySide.Worker)
            {
                lock (_stateLock)
                {
                    if (_terminalState is not null || runtimeGeneration != _runtimeGeneration || !_runtimeAttached)
                    {
                        return null;
                    }

                    // Retrying after any runtime protocol input could replay init or mix responses across callers.
                    _runtimeProtocolStarted = true;

                    return message;
                }
            }

            if (isFirstMessage)
            {
                lock (_stateLock)
                {
                    // A delayed read from a terminated session must not restore readiness.
                    if (_terminalState is not null)
                    {
                        return null;
                    }

                    if (message.StartStream is not { } startStream || string.IsNullOrWhiteSpace(startStream.WorkerId))
                    {
                        throw new InvalidDataException("The first worker message must be StartStream with a nonempty worker ID.");
                    }

                    stateManager.OnWorkerStartStream(id, startStream.WorkerId);
                    _startStream.TrySetResult(message.Clone());
                }
            }

            if (message.WorkerInitResponse is not null)
            {
                StreamingMessage sanitized = message.Clone();
                RuntimeWorkerAuthentication.RemoveAppProof(sanitized.WorkerInitResponse.Capabilities);

                return sanitized.WorkerInitResponse is { Result.Status: StatusResult.Types.Status.Success }
                    ? FinalizeCapabilities(sanitized)
                    : sanitized;
            }

            if (message.FunctionEnvironmentReloadResponse is { } reload)
            {
                StreamingMessage sanitized = message.Clone();
                RuntimeWorkerAuthentication.RemoveAppProof(sanitized.FunctionEnvironmentReloadResponse.Capabilities);
                if (reload is not { Result.Status: StatusResult.Types.Status.Success })
                {
                    sanitized.FunctionEnvironmentReloadResponse.Capabilities.Remove(WorkerHttpCapabilityProvider.HttpUriCapability);

                    return sanitized;
                }

                if (reload.Capabilities.TryGetValue(WorkerHttpCapabilityProvider.HttpUriCapability, out string? httpUri)
                    && !string.IsNullOrWhiteSpace(httpUri))
                {
                    Uri? destination = capabilityFinalizer.FinalizeCapabilities(sanitized.FunctionEnvironmentReloadResponse.Capabilities);
                    lock (_stateLock)
                    {
                        if (_terminalState is not null)
                        {
                            return null;
                        }

                        _workerHttpDestination = destination;
                    }
                }

                return sanitized;
            }

            return message;
        }

        private StreamingMessage? FinalizeCapabilities(StreamingMessage message)
        {
            FrozenDictionary<string, string>? capabilities;
            lock (_stateLock)
            {
                if (_terminalState is not null)
                {
                    return null;
                }

                capabilities = _capabilities;
            }

            // Only the worker reader finalizes capabilities. Keep cloning and logging outside the lifecycle lock.
            StreamingMessage finalized = message.Clone();
            if (capabilities is null)
            {
                Uri? destination = capabilityFinalizer.FinalizeCapabilities(finalized.WorkerInitResponse.Capabilities);
                ApplyFunctionGroupCapability(finalized.WorkerInitResponse.Capabilities);
                capabilities = finalized.WorkerInitResponse.Capabilities.ToFrozenDictionary(StringComparer.Ordinal);
                lock (_stateLock)
                {
                    if (_terminalState is not null)
                    {
                        return null;
                    }

                    // Publish the real destination before the rewritten response can reach the runtime.
                    _capabilities = capabilities;
                    _workerHttpDestination = destination;
                }
            }
            else
            {
                finalized.WorkerInitResponse.Capabilities.Clear();
                finalized.WorkerInitResponse.Capabilities.Add(capabilities);
            }

            return finalized;
        }

        /// <summary>
        /// Advertises the function group assigned to this session's worker, replacing any worker-supplied value.
        /// </summary>
        /// <remarks>
        /// Only WorkerProxy sets this capability, so any worker-supplied value is always discarded.
        /// The platform assigns the worker before the runtime initializes it. Without a live assignment for this session,
        /// the capability is omitted so the runtime never receives an unassigned or stale group.
        /// </remarks>
        private void ApplyFunctionGroupCapability(IDictionary<string, string> capabilities)
        {
            // Only WorkerProxy sets this capability; discard any worker-supplied value.
            capabilities.Remove(FunctionGroupNameCapability);

            WorkerPodState state = stateManager.State;
            if (state is { AssignmentState: WorkerAssignmentState.Ready, FunctionGroupName: { } functionGroupName }
                && state.SessionId == id)
            {
                capabilities[FunctionGroupNameCapability] = functionGroupName;
                return;
            }

            Log.FunctionGroupNotAdvertised(logger, id, state.AssignmentState);
        }

        private async Task WriteOutboundAsync(FunctionRpcRelaySide side, ChannelReader<StreamingMessage> source,
            IServerStreamWriter<StreamingMessage> responseStream, TrackedMessageWriter<StreamingMessage>? runtimeWrites,
            CancellationToken cancellationToken)
        {
            try
            {
                bool startSent = side is not FunctionRpcRelaySide.Runtime;
                Task<StreamingMessage> startReady = _startStream.Task.WaitAsync(cancellationToken);
                Task<bool>? queueReady = null;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!startSent && startReady.IsCompleted)
                    {
                        await responseStream.WriteAsync(await startReady, cancellationToken);
                        startSent = true;
                        continue;
                    }

                    if (!source.TryRead(out StreamingMessage? message))
                    {
                        queueReady ??= source.WaitToReadAsync(cancellationToken).AsTask();
                        // Proxy bootstrap must not wait for the customer's StartStream: identity may be needed to start it.
                        if (!startSent && await Task.WhenAny(queueReady, startReady) == startReady)
                        {
                            continue;
                        }
                        if (!await queueReady)
                        {
                            break;
                        }
                        queueReady = null;
                        continue;
                    }

                    if (runtimeWrites is not null && !runtimeWrites.Prepare(message))
                    {
                        continue;
                    }

                    // This is the only task that writes this response stream.
                    await responseStream.WriteAsync(message, cancellationToken);
                    if (runtimeWrites is not null)
                    {
                        runtimeWrites.Written(message);
                    }
                }
            }
            finally
            {
                if (runtimeWrites is not null)
                {
                    // A safe pre-init detach leaves the local worker and its queue available for a new runtime generation.
                    // This task is the sole reader. Drop abandoned HTTP payloads before releasing tracked ownership.
                    while (source.TryRead(out _))
                    {
                    }

                    runtimeWrites.Complete(new IOException("The HTTP proxy transport closed."));
                }
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

                return new FunctionRpcRelayTerminalState(id, FunctionRpcRelayTerminationReason.PeerClosed, side, Exception: null);
            }
            catch (OperationCanceledException exception)
            {
                if (_completion.Task.IsCompleted)
                {
                    return await _completion.Task;
                }

                return new FunctionRpcRelayTerminalState(id, FunctionRpcRelayTerminationReason.Canceled, side, exception);
            }
            catch (Exception exception) when (callCancellationToken.IsCancellationRequested)
            {
                return new FunctionRpcRelayTerminalState(id, FunctionRpcRelayTerminationReason.Canceled, side, exception);
            }
            catch (Exception exception)
            {
                return new FunctionRpcRelayTerminalState(id, FunctionRpcRelayTerminationReason.Faulted, side, exception);
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
                // Withdraw readiness before completing stream tasks or starting potentially slow teardown/logging.
                stateManager.OnSessionTerminated(id);
                _completion.SetResult(terminalState);
                _startStream.TrySetCanceled();
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
                Log.SessionFaulted(logger, terminalState.Exception, terminalState.SessionId, terminalState.Side);
            }
            else
            {
                Log.SessionTerminated(logger, terminalState.Exception, terminalState.SessionId, terminalState.Reason, terminalState.Side);
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

        private async Task ObserveStreamOperationAfterTerminationAsync(
            FunctionRpcRelaySide side, string operationName, Task operationTask, CancellationToken operationToken)
        {
            try
            {
                await operationTask;
            }
            catch (OperationCanceledException) when (_completion.Task.IsCompleted || operationToken.IsCancellationRequested)
            {
            }
            catch (ChannelClosedException) when (_completion.Task.IsCompleted)
            {
            }
            catch (Exception exception) when (_completion.Task.IsCompleted || operationToken.IsCancellationRequested)
            {
                Log.SecondaryStreamFailure(logger, exception, id, operationName, side);
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

    private static partial class Log
    {
        [LoggerMessage(4, LogLevel.Information,
            "Runtime disconnected before initialization for FunctionRpc relay session {SessionId}: {Reason}. The local worker is waiting for a replacement runtime.")]
        public static partial void RuntimeDetachedBeforeInitialization(ILogger logger, long sessionId, FunctionRpcRelayTerminationReason reason);

        [LoggerMessage(0, LogLevel.Warning, "FunctionRpc relay session {SessionId} faulted on the {Side} side.")]
        public static partial void SessionFaulted(ILogger logger, Exception? exception, long sessionId, FunctionRpcRelaySide? side);

        [LoggerMessage(1, LogLevel.Debug, "FunctionRpc relay session {SessionId} terminated with {Reason} on the {Side} side.")]
        public static partial void SessionTerminated(ILogger logger, Exception? exception, long sessionId,
            FunctionRpcRelayTerminationReason reason, FunctionRpcRelaySide? side);

        [LoggerMessage(2, LogLevel.Debug,
            "FunctionRpc relay session {SessionId} observed a secondary {OperationName} stream failure on the {Side} side.")]
        public static partial void SecondaryStreamFailure(ILogger logger, Exception exception, long sessionId, string operationName,
            FunctionRpcRelaySide side);

        [LoggerMessage(3, LogLevel.Warning,
            "FunctionRpc relay session {SessionId} received a WorkerInitResponse, but the session has no active worker " +
            "assignment (assignment state: {AssignmentState}). The FunctionGroupName capability was omitted, " +
            "so the Host will treat the worker's function group as unknown.")]
        public static partial void FunctionGroupNotAdvertised(ILogger logger, long sessionId, WorkerAssignmentState assignmentState);
    }
}
