// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Identity;
using Azure.Functions.WorkerProxy.Logging;
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
        private const string IsAlwaysReadyCapability = "IsAlwaysReady";
        private const string MaxHttpConcurrencyCapability = "MaxHttpConcurrency";
        private readonly Lock _stateLock = new();
        private readonly Channel<StreamingMessage> _toRuntime = CreateChannel(singleWriter: false);
        private readonly Channel<StreamingMessage> _toWorker = CreateChannel();
        // Linked call sources unregister when disposed, so cancellation can safely finish after this session is cleared.
        private readonly CancellationTokenSource _terminationSource = new();
        private readonly TaskCompletionSource<FunctionRpcRelayTerminalState> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<StreamingMessage> _startStream = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly long _started = Stopwatch.GetTimestamp();

        private FunctionRpcRelayTerminalState? _terminalState;
        private FrozenDictionary<string, string>? _capabilities;
        private Uri? _workerHttpDestination;
        private bool _runtimeAttached;
        private bool _workerAttached;
        private string? _initializationRequestId;
        private int _shutdownRequested;
        private volatile string? _workerId;
        private WorkerPodState _logState = stateManager.State;
        private long? _terminalRevision;

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
        /// <param name="runtimeIdentity">Optional identity from the Runtime-facing stream metadata.</param>
        /// <returns>The result of the reservation attempt.</returns>
        public FunctionRpcRelayAttachResult TryAttach(FunctionRpcRelaySide side, PodIdentity? runtimeIdentity = null)
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

                if (side == FunctionRpcRelaySide.Runtime && !stateManager.OnRuntimeAttached(id, runtimeIdentity))
                {
                    return FunctionRpcRelayAttachResult.Rejected;
                }

                if (side == FunctionRpcRelaySide.Worker && !stateManager.OnWorkerAttached(id))
                {
                    return FunctionRpcRelayAttachResult.Rejected;
                }

                SetAttachedLocked(side, value: true);
                return FunctionRpcRelayAttachResult.Attached;
            }
        }

        public WorkerSystemLogContext GetLogContext()
        {
            lock (_stateLock)
            {
                if (_terminalState is null)
                {
                    WorkerPodState current = stateManager.State;
                    if (current.SessionId is null || current.SessionId == id)
                    {
                        _logState = current;
                    }
                }

                return WorkerSystemLogContext.FromState(_logState) with
                {
                    WorkerId = _workerId,
                    ProxySessionId = id,
                    EventRevision = _terminalRevision
                };
            }
        }

        public void LogAttachment(FunctionRpcRelaySide side, bool released)
        {
            using IDisposable? scope = logger.BeginScope(GetLogContext() with
            {
                Operation = "RpcSession",
                Phase = released ? "Release" : "Attach",
                Side = side.ToString(),
                Outcome = released ? "Released" : "Accepted"
            });
            if (released)
            {
                Log.PeerReleased(logger, id, side);
            }
            else
            {
                Log.PeerAttached(logger, id, side);
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

            // Separate worker reads from initialization so an assignment wait cannot hide worker EOF.
            Channel<StreamingMessage>? workerInbound = side is FunctionRpcRelaySide.Worker ? CreateChannel() : null;
            Task readTask = ReadInboundAsync(
                side, requestStream, workerInbound?.Writer ?? destination, proxyConnection, linkedSource.Token);
            Task? processTask = workerInbound is null
                ? null
                : ProcessWorkerMessagesAsync(workerInbound.Reader, destination, linkedSource.Token);
            Task writeTask = WriteOutboundAsync(side, source, responseStream, runtimeWrites, linkedSource.Token, cancellationToken);
            Task firstCompletedTask = processTask is null
                ? await Task.WhenAny(readTask, writeTask)
                : await Task.WhenAny(readTask, processTask, writeTask);
            if (!_completion.Task.IsCompleted)
            {
                FunctionRpcRelayTerminalState terminalState = await ClassifyCompletionAsync(side, firstCompletedTask, cancellationToken);
                TryTerminate(terminalState with
                {
                    Phase = ReferenceEquals(firstCompletedTask, readTask) ? "Read"
                        : ReferenceEquals(firstCompletedTask, writeTask) ? "Write" : "Process"
                });
            }

            workerInbound?.Writer.TryComplete();
            await linkedSource.CancelAsync();
            // Reads can remain blocked until the transport releases them, so observe that task
            // out of band. The write must finish before gRPC writes trailers to the same response.
            _ = ObserveStreamOperationAfterTerminationAsync(side, "read", readTask, linkedSource.Token);
            if (processTask is not null)
            {
                _ = ObserveStreamOperationAfterTerminationAsync(side, "process", processTask, linkedSource.Token);
            }

            await ObserveStreamOperationAfterTerminationAsync(side, "write", writeTask, linkedSource.Token);

            return await _completion.Task;
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
                else
                {
                    stateManager.OnRuntimeDetached(id);
                }

                SignalReleasedIfCompleteLocked();
            }
        }

        /// <summary>
        /// Marks application shutdown before host cancellation can race with terminal classification.
        /// </summary>
        public void MarkShutdownRequested()
        {
            Interlocked.Exchange(ref _shutdownRequested, 1);
        }

        /// <summary>
        /// Records application shutdown as the terminal state for this session.
        /// </summary>
        public void RequestShutdown()
        {
            MarkShutdownRequested();
            TryTerminate(new FunctionRpcRelayTerminalState(id, FunctionRpcRelayTerminationReason.Shutdown, Side: null, Exception: null)
            {
                Phase = "Shutdown",
                CancellationSource = "Shutdown"
            });
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
            ChannelWriter<StreamingMessage> destination,
            TokenServiceProxyService.Connection? proxyConnection, CancellationToken cancellationToken)
        {
            bool isFirstMessage = true;
            while (await requestStream.MoveNext(cancellationToken))
            {
                StreamingMessage current = requestStream.Current;
                if (current.WorkerDraining is not null)
                {
                    if (side is not FunctionRpcRelaySide.Runtime)
                    {
                        throw new InvalidDataException("Worker draining controls are only accepted from the Host stream.");
                    }

                    lock (_stateLock)
                    {
                        if (_terminalState is not null || !_runtimeAttached)
                        {
                            return;
                        }
                    }

                    stateManager.OnWorkerDraining(id);
                    continue;
                }

                if (current.HttpProxy is { } control)
                {
                    if (side is FunctionRpcRelaySide.Runtime)
                    {
                        lock (_stateLock)
                        {
                            if (_terminalState is not null || !_runtimeAttached)
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

                StreamingMessage? message = ProcessInboundMessage(side, current, isFirstMessage);
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

        private StreamingMessage? ProcessInboundMessage(FunctionRpcRelaySide side, StreamingMessage message, bool isFirstMessage)
        {
            if (side != FunctionRpcRelaySide.Worker)
            {
                lock (_stateLock)
                {
                    if (_terminalState is not null || !_runtimeAttached)
                    {
                        return null;
                    }

                    if (message.WorkerInitRequest is not null)
                    {
                        _initializationRequestId = message.RequestId ?? string.Empty;
                    }
                }

                LogProtocolMessage(message, side, forwarded: false);
                return message;
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

                    if (message.StartStream is not { } startStream
                        || string.IsNullOrWhiteSpace(startStream.WorkerId))
                    {
                        throw new InvalidDataException("The first worker message must be StartStream with a nonempty worker ID.");
                    }

                    stateManager.OnWorkerStartStream(id, startStream.WorkerId);
                    _workerId = startStream.WorkerId;
                    _startStream.TrySetResult(message.Clone());
                }

                using IDisposable? scope = logger.BeginScope(GetLogContext() with
                {
                    Operation = "RpcSession",
                    Phase = "StartStream",
                    Outcome = "Validated",
                    Side = side.ToString(),
                    RequestId = message.RequestId
                });
                Log.StartStreamValidated(logger, id);
            }

            LogProtocolMessage(message, side, forwarded: false);
            if (message.WorkerInitResponse is not null)
            {
                StreamingMessage sanitized = message.Clone();
                RuntimeWorkerAuthentication.RemoveAppProof(sanitized.WorkerInitResponse.Capabilities);

                return sanitized;
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

                return sanitized;
            }

            return message;
        }

        private async Task ProcessWorkerMessagesAsync(ChannelReader<StreamingMessage> source, ChannelWriter<StreamingMessage> destination,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (StreamingMessage current in source.ReadAllAsync(cancellationToken))
                {
                    StreamingMessage? message = current.WorkerInitResponse is { Result.Status: StatusResult.Types.Status.Success }
                        ? await FinalizeCapabilitiesAsync(current, cancellationToken)
                        : current;
                    if (message is null)
                    {
                        return;
                    }

                    if (message.FunctionEnvironmentReloadResponse is { Result.Status: StatusResult.Types.Status.Success } reload
                        && reload.Capabilities.TryGetValue(WorkerHttpCapabilityProvider.HttpUriCapability, out string? httpUri)
                        && !string.IsNullOrWhiteSpace(httpUri))
                    {
                        var (_, httpProxyEndpoint) = stateManager.GetWorkerCapabilitySnapshot(id);
                        Uri? workerDestination = capabilityFinalizer.FinalizeCapabilities(reload.Capabilities, httpProxyEndpoint);
                        lock (_stateLock)
                        {
                            if (_terminalState is not null)
                            {
                                return;
                            }

                            _workerHttpDestination = workerDestination;
                        }
                    }

                    await destination.WriteAsync(message, cancellationToken);
                }
            }
            finally
            {
                // This task owns queued messages; release them even if a finalizer faulted or initialization was canceled.
                while (source.TryRead(out _))
                {
                }
            }
        }

        /// <summary>
        /// Waits for this worker's assignment before freezing the initial successful response's authoritative capabilities.
        /// The separate processor leaves relay readers available while assignment is pending.
        /// </summary>
        private async Task<StreamingMessage?> FinalizeCapabilitiesAsync(StreamingMessage message, CancellationToken cancellationToken)
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

            // Only the worker processor finalizes capabilities. Keep cloning and logging outside the lifecycle lock.
            StreamingMessage finalized = message.Clone();
            if (capabilities is null)
            {
                (WorkerPodState state, string? httpProxyEndpoint) = stateManager.GetWorkerCapabilitySnapshot(id);
                // Every worker needs assigned group identity, including workers without an HTTP endpoint.
                while (state.SessionId == id && state.IsWorkerAttached && state.AssignmentState == WorkerAssignmentState.Unassigned)
                {
                    await stateManager.WaitForChangeAsync(state.Revision, cancellationToken);
                    (state, httpProxyEndpoint) = stateManager.GetWorkerCapabilitySnapshot(id);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (state.SessionId != id || !state.IsWorkerAttached || state.AssignmentState != WorkerAssignmentState.Ready)
                {
                    throw new InvalidOperationException("The worker session has no ready assignment for capability finalization.");
                }

                Uri? destination = capabilityFinalizer.FinalizeCapabilities(
                    finalized.WorkerInitResponse.Capabilities, httpProxyEndpoint);
                ApplyAssignmentCapabilities(finalized.WorkerInitResponse.Capabilities, state);
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
        /// Advertises the assigned group, HTTP concurrency, and always-ready policy, replacing worker-supplied values.
        /// </summary>
        /// <remarks>
        /// Only WorkerProxy sets these capabilities, so worker-supplied values are always discarded.
        /// The platform assigns the worker before the runtime initializes it. Without a live assignment for this session,
        /// the capabilities are omitted so the runtime never receives unassigned or stale policy.
        /// </remarks>
        private void ApplyAssignmentCapabilities(IDictionary<string, string> capabilities, WorkerPodState state)
        {
            // Only WorkerProxy sets this capability; discard any worker-supplied value.
            capabilities.Remove(FunctionGroupNameCapability);
            capabilities.Remove(IsAlwaysReadyCapability);
            capabilities.Remove(MaxHttpConcurrencyCapability);

            if (state is { AssignmentState: WorkerAssignmentState.Ready, FunctionGroupName: { } functionGroupName }
                && state.SessionId == id)
            {
                capabilities[FunctionGroupNameCapability] = functionGroupName;
                capabilities[IsAlwaysReadyCapability] = state.IsAlwaysReady is true ? bool.TrueString : bool.FalseString;
                if (string.Equals(functionGroupName, "http", StringComparison.OrdinalIgnoreCase) && state.MaxHttpConcurrency is { } concurrency)
                {
                    capabilities[MaxHttpConcurrencyCapability] = concurrency.ToString(CultureInfo.InvariantCulture);
                }

                return;
            }

            using IDisposable? scope = logger.BeginScope(GetLogContext());
            Log.FunctionGroupNotAdvertised(logger, id, state.AssignmentState);
        }

        private async Task WriteOutboundAsync(FunctionRpcRelaySide side, ChannelReader<StreamingMessage> source,
            IServerStreamWriter<StreamingMessage> responseStream, TrackedMessageWriter<StreamingMessage>? runtimeWrites,
            CancellationToken cancellationToken, CancellationToken callCancellationToken)
        {
            StreamingMessage? writing = null;
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
                        writing = await startReady;
                        await responseStream.WriteAsync(writing, cancellationToken);
                        writing = null;
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
                    writing = message;
                    await responseStream.WriteAsync(message, cancellationToken);
                    writing = null;
                    if (runtimeWrites is not null)
                    {
                        runtimeWrites.Written(message);
                        PublishRuntimeInitialization(message);
                    }

                    LogProtocolMessage(message, side, forwarded: true);
                }
            }
            catch (Exception exception)
            {
                // Preserve the originating failure before queue closure can fault the Worker processor.
                FunctionRpcRelayTerminationReason reason = exception is OperationCanceledException || cancellationToken.IsCancellationRequested
                    ? FunctionRpcRelayTerminationReason.Canceled
                    : FunctionRpcRelayTerminationReason.Faulted;
                TryTerminate(new FunctionRpcRelayTerminalState(id, reason, side, exception)
                {
                    Phase = "Write",
                    CancellationSource = reason is FunctionRpcRelayTerminationReason.Canceled
                        ? Volatile.Read(ref _shutdownRequested) != 0 ? "Shutdown"
                            : callCancellationToken.IsCancellationRequested ? "PeerCall" : "Unknown" : null,
                    RequestId = writing?.RequestId,
                    InvocationId = writing?.InvocationRequest?.InvocationId ?? writing?.InvocationResponse?.InvocationId,
                    FunctionId = writing?.InvocationRequest?.FunctionId ?? writing?.FunctionLoadRequest?.FunctionId
                        ?? writing?.FunctionLoadResponse?.FunctionId
                });
                throw;
            }
            finally
            {
                if (runtimeWrites is not null)
                {
                    _toRuntime.Writer.TryComplete();
                    // This task is the sole reader. Drop abandoned HTTP payloads before releasing tracked ownership.
                    while (source.TryRead(out _))
                    {
                    }

                    runtimeWrites.Complete(new IOException("The HTTP proxy transport closed."));
                }
            }
        }

        private void PublishRuntimeInitialization(StreamingMessage message)
        {
            if (message.WorkerInitResponse is not { Result.Status: StatusResult.Types.Status.Success })
            {
                return;
            }

            lock (_stateLock)
            {
                if (_terminalState is null && _initializationRequestId is not null &&
                    string.Equals(_initializationRequestId, message.RequestId ?? string.Empty, StringComparison.Ordinal))
                {
                    _initializationRequestId = null;
                    stateManager.OnRuntimeInitialized(id);
                }
            }
        }

        private void LogProtocolMessage(StreamingMessage message, FunctionRpcRelaySide side, bool forwarded)
        {
            if (message.FunctionLoadRequestCollection is { } requests)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    using IDisposable? batchScope = logger.BeginScope(GetLogContext() with
                    {
                        Operation = "FunctionLoad",
                        Phase = forwarded ? "RequestForwarded" : "RequestReceived",
                        Side = side.ToString(),
                        RequestId = message.RequestId,
                        FunctionCount = requests.FunctionLoadRequests.Count
                    });
                    Log.FunctionLoadBatchRequested(logger, forwarded ? "RequestForwarded" : "RequestReceived", requests.FunctionLoadRequests.Count);
                }

                return;
            }

            if (message.FunctionLoadResponseCollection is { } responses)
            {
                if (!logger.IsEnabled(LogLevel.Information) && !logger.IsEnabled(LogLevel.Warning))
                {
                    return;
                }

                int unsuccessful = 0;
                foreach (FunctionLoadResponse response in responses.FunctionLoadResponses)
                {
                    if (response.Result is not { Status: StatusResult.Types.Status.Success })
                    {
                        unsuccessful++;
                    }
                }

                LogLevel batchLevel = unsuccessful > 0 ? LogLevel.Warning : LogLevel.Information;
                if (logger.IsEnabled(batchLevel))
                {
                    using IDisposable? batchScope = logger.BeginScope(GetLogContext() with
                    {
                        Operation = "FunctionLoad",
                        Phase = forwarded ? "ResponseForwarded" : "ResponseReceived",
                        Outcome = unsuccessful > 0 ? "Unsuccessful" : "Success",
                        ReasonCode = unsuccessful > 0 ? "BatchResult" : null,
                        Side = side.ToString(),
                        RequestId = message.RequestId,
                        FunctionCount = responses.FunctionLoadResponses.Count,
                        UnsuccessfulCount = unsuccessful
                    });
                    string batchStage = forwarded ? "ResponseForwarded" : "ResponseReceived";
                    if (unsuccessful > 0)
                    {
                        Log.FunctionLoadBatchFailed(logger, batchStage, responses.FunctionLoadResponses.Count, unsuccessful);
                    }
                    else
                    {
                        Log.FunctionLoadBatchCompleted(logger, batchStage, responses.FunctionLoadResponses.Count, unsuccessful);
                    }
                }

                return;
            }

            (string Operation, bool Response, StatusResult? Result)? operation = message.ContentCase switch
            {
                StreamingMessage.ContentOneofCase.WorkerInitRequest => ("Initialization", false, null),
                StreamingMessage.ContentOneofCase.WorkerInitResponse => ("Initialization", true, message.WorkerInitResponse.Result),
                StreamingMessage.ContentOneofCase.FunctionLoadRequest => ("FunctionLoad", false, null),
                StreamingMessage.ContentOneofCase.FunctionLoadResponse => ("FunctionLoad", true, message.FunctionLoadResponse.Result),
                StreamingMessage.ContentOneofCase.InvocationResponse
                    when !forwarded && message.InvocationResponse.Result is not { Status: StatusResult.Types.Status.Success }
                    => ("Invocation", true, message.InvocationResponse.Result),
                _ => null
            };
            if (operation is not { } value)
            {
                return;
            }

            LogLevel level = value.Response && value.Result is not { Status: StatusResult.Types.Status.Success }
                ? LogLevel.Warning : LogLevel.Information;
            if (!logger.IsEnabled(level))
            {
                return;
            }

            string stage = (value.Response, forwarded) switch
            {
                (false, false) => "RequestReceived",
                (false, true) => "RequestForwarded",
                (true, false) => "ResponseReceived",
                (true, true) => "ResponseForwarded"
            };
            string? rpcStatus = value.Result?.Status.ToString();
            using IDisposable? scope = logger.BeginScope(GetLogContext() with
            {
                Operation = value.Operation,
                Phase = stage,
                Outcome = value.Response ? level is LogLevel.Warning ? "Unsuccessful" : "Success" : null,
                ReasonCode = value.Response ? value.Result is null ? "MissingResult" : "ProtocolResult" : null,
                Side = side.ToString(),
                RpcStatus = rpcStatus,
                RequestId = message.RequestId,
                FunctionId = message.FunctionLoadRequest?.FunctionId ?? message.FunctionLoadResponse?.FunctionId,
                InvocationId = message.InvocationResponse?.InvocationId
            });
            if (message.InvocationResponse is not null)
            {
                Log.InvocationResponseFailed(logger, rpcStatus);
                return;
            }

            if (level == LogLevel.Warning)
            {
                Log.ProtocolMessageFailed(logger, value.Operation, stage, rpcStatus);
            }
            else
            {
                Log.ProtocolMessageObserved(logger, value.Operation, stage, rpcStatus);
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

                return new FunctionRpcRelayTerminalState(id, FunctionRpcRelayTerminationReason.Canceled, side, exception)
                {
                    CancellationSource = Volatile.Read(ref _shutdownRequested) != 0 ? "Shutdown"
                        : callCancellationToken.IsCancellationRequested ? "PeerCall" : "Unknown"
                };
            }
            catch (Exception exception) when (callCancellationToken.IsCancellationRequested)
            {
                return new FunctionRpcRelayTerminalState(id, FunctionRpcRelayTerminationReason.Canceled, side, exception)
                {
                    CancellationSource = Volatile.Read(ref _shutdownRequested) != 0 ? "Shutdown" : "PeerCall"
                };
            }
            catch (Exception exception)
            {
                return new FunctionRpcRelayTerminalState(id, FunctionRpcRelayTerminationReason.Faulted, side, exception);
            }
        }

        private bool TryTerminate(FunctionRpcRelayTerminalState terminalState)
        {
            int? pendingMessages;
            lock (_stateLock)
            {
                if (_terminalState is not null)
                {
                    return false;
                }

                _ = GetLogContext();
                pendingMessages = _toWorker.Reader.CanCount && _toRuntime.Reader.CanCount
                    ? _toWorker.Reader.Count + _toRuntime.Reader.Count : null;
                bool runtimePeerTerminated = Volatile.Read(ref _shutdownRequested) == 0 &&
                    terminalState.Side == FunctionRpcRelaySide.Runtime &&
                    terminalState.Reason is FunctionRpcRelayTerminationReason.PeerClosed
                        or FunctionRpcRelayTerminationReason.Canceled
                        or FunctionRpcRelayTerminationReason.Faulted;
                // Withdraw readiness before completing stream tasks or starting potentially slow teardown/logging.
                if (runtimePeerTerminated)
                {
                    stateManager.RequestRemoval();
                }
                else
                {
                    bool assignmentFailed = terminalState is
                    {
                        Reason: FunctionRpcRelayTerminationReason.Faulted,
                        Side: FunctionRpcRelaySide.Worker,
                        Exception: InvalidDataException
                    };
                    stateManager.OnSessionTerminated(id, assignmentFailed);
                }

                _terminalState = terminalState;
                _terminalRevision = stateManager.State.Revision;
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

            using IDisposable? scope = logger.BeginScope(GetLogContext() with
            {
                Operation = "RpcSession",
                Phase = terminalState.Phase,
                Outcome = "Terminated",
                ReasonCode = terminalState.Reason.ToString(),
                Side = terminalState.Side?.ToString(),
                CancellationSource = terminalState.CancellationSource,
                RequestId = terminalState.RequestId,
                InvocationId = terminalState.InvocationId,
                FunctionId = terminalState.FunctionId,
                ElapsedMs = Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
                PendingMessages = pendingMessages
            });
            if (terminalState.Reason == FunctionRpcRelayTerminationReason.Faulted)
            {
                Log.SessionFaulted(logger, terminalState.Exception?.GetType().Name, terminalState.SessionId, terminalState.Side);
            }
            else
            {
                Log.SessionTerminated(logger, terminalState.SessionId, terminalState.Reason, terminalState.Side);
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
                using IDisposable? scope = logger.BeginScope(GetLogContext() with
                {
                    Operation = "RpcSession",
                    Phase = operationName,
                    Side = side.ToString(),
                    Outcome = "SecondaryFailure",
                    ReasonCode = "StreamFailure"
                });
                Log.SecondaryStreamFailure(logger, exception.GetType().Name, id, operationName, side);
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
        [LoggerMessage(0, LogLevel.Warning, "FunctionRpc relay session {SessionId} faulted on the {Side} side ({ExceptionType}).")]
        public static partial void SessionFaulted(ILogger logger, string? exceptionType, long sessionId, FunctionRpcRelaySide? side);

        [LoggerMessage(1, LogLevel.Information, "FunctionRpc relay session {SessionId} terminated with {Reason} on the {Side} side.")]
        public static partial void SessionTerminated(ILogger logger, long sessionId,
            FunctionRpcRelayTerminationReason reason, FunctionRpcRelaySide? side);

        [LoggerMessage(2, LogLevel.Debug,
            "FunctionRpc relay session {SessionId} observed a secondary {OperationName} stream failure on the {Side} side ({ExceptionType}).")]
        public static partial void SecondaryStreamFailure(ILogger logger, string exceptionType, long sessionId, string operationName,
            FunctionRpcRelaySide side);

        [LoggerMessage(3, LogLevel.Warning,
            "FunctionRpc relay session {SessionId} received a WorkerInitResponse, but the session has no active worker " +
            "assignment (assignment state: {AssignmentState}). The FunctionGroupName capability was omitted, " +
            "so the Host will treat the worker's function group as unknown.")]
        public static partial void FunctionGroupNotAdvertised(ILogger logger, long sessionId, WorkerAssignmentState assignmentState);

        [LoggerMessage(4, LogLevel.Information, "FunctionRpc session {SessionId} accepted the {Side} peer.")]
        public static partial void PeerAttached(ILogger logger, long sessionId, FunctionRpcRelaySide side);

        [LoggerMessage(5, LogLevel.Warning, "FunctionRpc rejected the {Side} peer: {Reason}.")]
        public static partial void AttachmentRejected(ILogger logger, FunctionRpcRelaySide side, FunctionRpcRelayAttachmentFailure reason);

        [LoggerMessage(6, LogLevel.Information, "FunctionRpc session {SessionId} validated worker StartStream.")]
        public static partial void StartStreamValidated(ILogger logger, long sessionId);

        [LoggerMessage(7, LogLevel.Information, "FunctionRpc session {SessionId} released the {Side} peer.")]
        public static partial void PeerReleased(ILogger logger, long sessionId, FunctionRpcRelaySide side);

        [LoggerMessage(8, LogLevel.Information, "FunctionRpc {Operation} reached {Stage} with RPC status {RpcStatus}.")]
        public static partial void ProtocolMessageObserved(ILogger logger, string operation, string stage, string? rpcStatus);

        [LoggerMessage(8, LogLevel.Warning, "FunctionRpc {Operation} reached {Stage} with RPC status {RpcStatus}.",
            EventName = nameof(ProtocolMessageObserved))]
        public static partial void ProtocolMessageFailed(ILogger logger, string operation, string stage, string? rpcStatus);

        [LoggerMessage(9, LogLevel.Information, "FunctionRpc FunctionLoad batch reached {Stage} with {FunctionCount} functions.")]
        public static partial void FunctionLoadBatchRequested(ILogger logger, string stage, int functionCount);

        [LoggerMessage(10, LogLevel.Information,
            "FunctionRpc FunctionLoad batch reached {Stage} with {FunctionCount} results; {UnsuccessfulCount} unsuccessful or missing statuses.")]
        public static partial void FunctionLoadBatchCompleted(ILogger logger, string stage, int functionCount, int unsuccessfulCount);

        [LoggerMessage(10, LogLevel.Warning,
            "FunctionRpc FunctionLoad batch reached {Stage} with {FunctionCount} results; {UnsuccessfulCount} unsuccessful or missing statuses.",
            EventName = nameof(FunctionLoadBatchCompleted))]
        public static partial void FunctionLoadBatchFailed(ILogger logger, string stage, int functionCount, int unsuccessfulCount);

        [LoggerMessage(11, LogLevel.Warning, "FunctionRpc observed an unsuccessful invocation response with RPC status {RpcStatus}.")]
        public static partial void InvocationResponseFailed(ILogger logger, string? rpcStatus);
    }
}
