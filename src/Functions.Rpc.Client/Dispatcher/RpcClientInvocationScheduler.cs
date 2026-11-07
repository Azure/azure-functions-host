// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors.Internal;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Schedules invocation admission using group-aware round-robin selection and HTTP FIFO turns.
/// </summary>
/// <remarks>
/// One dispatcher owns this scheduler. It borrows root-owned channels and never disposes them or owns their permits.
/// FIFO queueing and channel waiting share each caller's cancellation scope and matching-worker readiness deadline.
/// HTTP execution remains concurrent up to channel-owned limits; other function groups bypass HTTP FIFO admission.
/// </remarks>
/// <param name="channelRegistry">The root registry supplying initialized and ready worker channels.</param>
/// <param name="logger">The owning dispatcher's logger, preserving its logging category.</param>
/// <param name="channelWaitTimeout">The readiness budget while matching workers are absent, excluding capacity waits.</param>
/// <param name="invocationTracker">The optional per-worker billing attribution tracker.</param>
internal sealed partial class RpcClientInvocationScheduler(
    IWorkerChannelRegistry channelRegistry,
    ILogger<RpcClientFunctionInvocationDispatcher> logger,
    TimeSpan channelWaitTimeout,
    IWorkerInvocationTracker invocationTracker = null) : IDisposable
{
    private readonly IWorkerChannelRegistry _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
    // Admission may race Stop while creating linked tokens, so keep the canceled source alive until the scheduler is collected.
    private readonly CancellationTokenSource _stoppedSource = new();
    private readonly ILogger<RpcClientFunctionInvocationDispatcher> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TimeSpan _channelWaitTimeout = channelWaitTimeout;
    private readonly IWorkerInvocationTracker _invocationTracker = invocationTracker;
    private readonly ConcurrentDictionary<string, GroupSelectionState> _groupSelections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrencyLimiter _httpAdmission = new(new()
    {
        PermitLimit = 1,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = int.MaxValue,
    });

    private int _stopped;

    /// <summary>
    /// Admits a validated invocation to its matching worker group. The owning dispatcher checks its public lifecycle first.
    /// Accepted work remains channel-owned; returning does not wait for worker execution or HTTP response completion.
    /// </summary>
    internal Task InvokeAsync(ScriptInvocationContext invocationContext)
    {
        string functionGroup = GetFunctionGroup(invocationContext.FunctionMetadata);
        if (string.Equals(functionGroup, FunctionGroups.Http, StringComparison.OrdinalIgnoreCase))
        {
            return InvokeHttpInOrderAsync(invocationContext, functionGroup);
        }

        Task changed = _channelRegistry.GetChangeTask();
        ValueTask<IReadOnlyList<WorkerChannel>> ready = _channelRegistry.GetReadyChannelsAsync();
        if (ready.IsCompletedSuccessfully)
        {
            IReadOnlyList<WorkerChannel> snapshot = ready.Result;
            if (TryDispatchInvocation(invocationContext, functionGroup, snapshot, CancellationToken.None, out _))
            {
                return Task.CompletedTask;
            }

            return InvokeWhenChannelIsReadyAsync(invocationContext, functionGroup, new(snapshot), changed);
        }

        return InvokeWhenChannelIsReadyAsync(invocationContext, functionGroup, ready, changed);
    }

    /// <summary>
    /// Cancels pending queue/channel waits and disposes the FIFO gate without releasing accepted worker permits.
    /// Repeated calls are ignored.
    /// </summary>
    internal void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        try
        {
            _stoppedSource.Cancel();
        }
        finally
        {
            _httpAdmission.Dispose();
        }
    }

    /// <summary>
    /// Ends admission without disposing borrowed channels; repeated stops and disposal are harmless.
    /// </summary>
    public void Dispose() => Stop();

    /// <summary>
    /// Holds the HTTP FIFO turn only until a worker accepts the invocation or admission fails.
    /// Disposing the turn lets the next caller select a worker; it does not release the accepted invocation's worker slot.
    /// </summary>
    private async Task InvokeHttpInOrderAsync(ScriptInvocationContext context, string functionGroup)
    {
        using CancellationTokenSource operation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _stoppedSource.Token);
        (RateLimitLease lease, long? unavailableSince) = await AcquireHttpAdmissionAsync(context, operation);
        using RateLimitLease turn = lease;
        if (!turn.IsAcquired)
        {
            operation.Token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The HTTP invocation admission queue is unavailable.");
        }

        operation.Token.ThrowIfCancellationRequested();
        Task changed = _channelRegistry.GetChangeTask();
        ValueTask<IReadOnlyList<WorkerChannel>> ready = _channelRegistry.GetReadyChannelsAsync();
        if (ready.IsCompletedSuccessfully)
        {
            IReadOnlyList<WorkerChannel> snapshot = ready.Result;
            if (TryDispatchInvocation(context, functionGroup, snapshot, operation.Token, out _))
            {
                return;
            }

            ready = new(snapshot);
        }

        await WaitForChannelAsync(context, functionGroup, ready, changed, operation.Token, unavailableSince);
    }

    /// <summary>
    /// Acquires the next HTTP admission turn while preserving the queued caller's matching-worker readiness deadline.
    /// Busy matching workers pause that deadline; cancellation or failure settles any raced grant before returning.
    /// </summary>
    /// <param name="context">The invocation waiting for its admission turn.</param>
    /// <param name="operation">The cancellation scope shared by FIFO queueing and subsequent channel waiting.</param>
    /// <returns>The caller-owned turn and any matching-worker absence timestamp carried into channel waiting.</returns>
    private async ValueTask<(RateLimitLease Lease, long? UnavailableSince)> AcquireHttpAdmissionAsync(
        ScriptInvocationContext context, CancellationTokenSource operation)
    {
        ValueTask<RateLimitLease> acquisition = _httpAdmission.AcquireAsync(1, operation.Token);
        if (acquisition.IsCompletedSuccessfully)
        {
            return (acquisition.Result, null);
        }

        Task<RateLimitLease> pending = acquisition.AsTask();
        long? unavailableSince = null;
        bool transferred = false;
        try
        {
            while (!pending.IsCompleted)
            {
                operation.Token.ThrowIfCancellationRequested();
                Task changed = _channelRegistry.GetChangeTask();
                ValueTask<IReadOnlyList<WorkerChannel>> ready = _channelRegistry.GetReadyChannelsAsync();
                if (ready.IsCompleted && !ready.IsCompletedSuccessfully)
                {
                    await ready;
                }

                bool hasReadyWorker = ready.IsCompletedSuccessfully && ready.Result.Any(channel => IsEligible(channel, FunctionGroups.Http));
                if (hasReadyWorker)
                {
                    unavailableSince = null;
                    await Task.WhenAny(pending, changed).WaitAsync(operation.Token);
                }
                else
                {
                    // Queue position does not pause a missing-worker deadline. Saturation has no readiness deadline.
                    await Task.WhenAny(pending, changed).WaitAsync(RemainingReadinessWait(ref unavailableSince), operation.Token);
                }
            }

            RateLimitLease lease = await pending;
            ValueTask<IReadOnlyList<WorkerChannel>> current = _channelRegistry.GetReadyChannelsAsync();
            if (current.IsCompletedSuccessfully && current.Result.Any(channel => IsEligible(channel, FunctionGroups.Http)))
            {
                unavailableSince = null;
            }
            else if (unavailableSince is not null)
            {
                RemainingReadinessWait(ref unavailableSince);
            }

            transferred = true;
            return (lease, unavailableSince);
        }
        catch (TimeoutException)
        {
            throw MissingWorkerTimeout(context, FunctionGroups.Http);
        }
        finally
        {
            if (!transferred)
            {
                // Cancellation can race a granted turn. Settle that acquisition without touching worker ownership.
                operation.Cancel();
                try
                {
                    using RateLimitLease abandoned = await pending;
                }
                catch (OperationCanceledException) when (operation.IsCancellationRequested)
                {
                }
            }
        }
    }

    /// <summary>
    /// Tries the round-robin-preferred worker, then each other matching worker once using the captured snapshot.
    /// Full workers are skipped without advancing the shared cursor again during this fallback scan.
    /// Non-capacity rejection is surfaced through the invocation result rather than retrying accepted work.
    /// </summary>
    /// <param name="context">The invocation whose ownership is transferred on successful posting.</param>
    /// <param name="functionGroup">The group derived from validated function metadata.</param>
    /// <param name="channels">The current registry snapshot, which may contain unrelated or stale channels.</param>
    /// <param name="cancellationToken">Cancels selection before ownership is acquired.</param>
    /// <param name="atCapacity">True when at least one matching ready worker rejected admission because it was full.</param>
    /// <returns>True when dispatch handling finished, including a rejection reported through the invocation result.</returns>
    private bool TryDispatchInvocation(
        ScriptInvocationContext context,
        string functionGroup,
        IReadOnlyList<WorkerChannel> channels,
        CancellationToken cancellationToken,
        out bool atCapacity)
    {
        atCapacity = false;
        if (!TryGetReadyChannel(channels, functionGroup, out WorkerChannel preferred))
        {
            return false;
        }

        if (DispatchInvocation(context, (RpcClientWorkerChannel)preferred, cancellationToken, out bool full))
        {
            return true;
        }

        atCapacity = full;
        foreach (WorkerChannel candidate in channels)
        {
            if (ReferenceEquals(candidate, preferred) || !IsEligible(candidate, functionGroup))
            {
                continue;
            }

            if (DispatchInvocation(context, (RpcClientWorkerChannel)candidate, cancellationToken, out full))
            {
                return true;
            }

            atCapacity |= full;
        }

        return false;
    }

    /// <summary>
    /// Preserves billing attribution and atomic readiness checks against channel retirement while reserving a worker slot.
    /// Full workers decline without creating billing activity or transferring invocation ownership.
    /// </summary>
    private bool DispatchInvocation(
        ScriptInvocationContext context, RpcClientWorkerChannel channel, CancellationToken cancellationToken, out bool atCapacity)
    {
        atCapacity = false;
        cancellationToken.ThrowIfCancellationRequested();
        using FunctionInvoker.Scope scope = FunctionInvoker.BeginSystemScope();
        lock (channel.LifecycleLock)
        {
            if (!channel.IsReadyForInvocations)
            {
                return false;
            }

            string invocationId = context.ExecutionContext.InvocationId.ToString();
            bool full = false;
            bool accepted = _invocationTracker is null
                ? channel.TryAcceptInvocation(context, out full)
                : _invocationTracker.TryDispatchInvocation(invocationId, channel.Id, () => channel.TryAcceptInvocation(context, out full));
            atCapacity = full;
            if (!accepted)
            {
                if (full || channel.IsTerminated || channel.IsDraining)
                {
                    return false;
                }

                context.ResultSource.TrySetException(
                    new InvalidOperationException($"Worker '{channel.Id}' is no longer accepting invocations or is unavailable for attribution."));
                return true;
            }

            if (_invocationTracker is not null)
            {
                _ = context.ResultSource.Task.ContinueWith(
                    result =>
                    {
                        try
                        {
                            _invocationTracker.InvocationCompleted(invocationId, result.IsCompletedSuccessfully);
                        }
                        catch (Exception exception) when (!Utility.IsFatal(exception))
                        {
                            Log.InvocationAccountingFailed(_logger, exception, invocationId, channel.Id);
                        }
                    },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                Log.PostingInvocation(_logger, context.ExecutionContext.InvocationId, channel.Id);
            }

            return true;
        }
    }

    /// <summary>
    /// Reads the Host-derived group captured during initialization; unknown definitions are classified without mutating shared metadata.
    /// </summary>
    internal static string GetFunctionGroup(FunctionMetadata metadata)
        => metadata.Properties.TryGetValue(RpcClientScriptHostLifecycleService.FunctionGroupKey, out object value) && value is string group
            ? group
            : metadata.GetFunctionGroup();

    /// <summary>
    /// Selects a matching ready channel in one bounded scan, with independent cursors and locks per group.
    /// </summary>
    internal bool TryGetReadyChannel(IReadOnlyList<WorkerChannel> channels, string functionGroup, [NotNullWhen(true)] out WorkerChannel channel)
    {
        channel = null;
        if (channels.Count == 0)
        {
            return false;
        }

        if (channels.Count == 1)
        {
            if (!IsEligible(channels[0], functionGroup))
            {
                return false;
            }

            channel = channels[0];
            return true;
        }

        GroupSelectionState selection = _groupSelections.GetOrAdd(functionGroup, static _ => new());
        lock (selection.Gate)
        {
            int next = selection.NextIndex % channels.Count;
            for (int remaining = channels.Count; remaining > 0; remaining--)
            {
                WorkerChannel candidate = channels[next];
                next = next == channels.Count - 1 ? 0 : next + 1;
                if (IsEligible(candidate, functionGroup))
                {
                    selection.NextIndex = next;
                    channel = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Creates the cancellation scope for non-HTTP admission that could not complete synchronously.
    /// The initial registry notification precedes the snapshot so matching-worker arrivals cannot be missed.
    /// </summary>
    private async Task InvokeWhenChannelIsReadyAsync(
        ScriptInvocationContext context, string functionGroup, ValueTask<IReadOnlyList<WorkerChannel>> ready, Task changed)
    {
        using CancellationTokenSource operation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _stoppedSource.Token);
        await WaitForChannelAsync(context, functionGroup, ready, changed, operation.Token);
    }

    /// <summary>
    /// Retries matching-group admission when registry membership/readiness or an eligible worker's capacity changes.
    /// Missing matching workers consume the readiness budget; ready but saturated workers have no readiness timeout.
    /// </summary>
    /// <param name="context">The invocation awaiting worker admission.</param>
    /// <param name="functionGroup">The required immutable assignment group.</param>
    /// <param name="ready">The initial snapshot or pending readiness task.</param>
    /// <param name="changed">The registry notification captured before sampling that snapshot.</param>
    /// <param name="cancellationToken">Cancels this caller's wait without canceling shared notifications.</param>
    /// <param name="unavailableSince">A queued caller's existing absence timestamp, or null until matching workers are absent.</param>
    private async Task WaitForChannelAsync(
        ScriptInvocationContext context,
        string functionGroup,
        ValueTask<IReadOnlyList<WorkerChannel>> ready,
        Task changed,
        CancellationToken cancellationToken,
        long? unavailableSince = null)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<WorkerChannel> channels = ready.IsCompletedSuccessfully
                    ? ready.Result
                    : await ready.AsTask().WaitAsync(RemainingReadinessWait(ref unavailableSince), cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (TryDispatchInvocation(context, functionGroup, channels, cancellationToken, out bool atCapacity))
                {
                    return;
                }

                if (atCapacity)
                {
                    unavailableSince = null;
                    // Availability is level-triggered: subscribe after failed admission so a raced release completes immediately.
                    List<Task> changes = [changed];
                    foreach (WorkerChannel channel in channels)
                    {
                        if (IsEligible(channel, functionGroup))
                        {
                            changes.Add(((RpcClientWorkerChannel)channel).GetCapacityAvailableAsync());
                        }
                    }

                    await Task.WhenAny(changes).WaitAsync(cancellationToken);
                }
                else
                {
                    // Wrong-group workers can stay globally ready; only a registry change can make this group ready.
                    await changed.WaitAsync(RemainingReadinessWait(ref unavailableSince), cancellationToken);
                }

                changed = _channelRegistry.GetChangeTask();
                ready = _channelRegistry.GetReadyChannelsAsync();
            }
        }
        catch (TimeoutException)
        {
            throw MissingWorkerTimeout(context, functionGroup);
        }
    }

    /// <summary>
    /// Starts or continues a monotonic matching-worker absence interval and throws when its readiness budget expires.
    /// Callers reset the timestamp when matching workers are ready so queue/capacity time is not counted as absence.
    /// </summary>
    private TimeSpan RemainingReadinessWait(ref long? unavailableSince)
    {
        unavailableSince ??= Stopwatch.GetTimestamp();
        TimeSpan remaining = _channelWaitTimeout - Stopwatch.GetElapsedTime(unavailableSince.Value);
        if (remaining <= TimeSpan.Zero)
        {
            throw new TimeoutException();
        }

        return remaining;
    }

    /// <summary>
    /// Identifies both the function and its required group when no eligible worker becomes ready before the deadline.
    /// </summary>
    private WorkerChannelUnavailableException MissingWorkerTimeout(ScriptInvocationContext context, string functionGroup)
        => new($"No worker channel availalble for function '{context.FunctionMetadata.Name}' in group '{functionGroup}' within {_channelWaitTimeout}.");

    /// <summary>
    /// Requires a ready serving channel with the same assigned group, using case-insensitive platform identity.
    /// Validation workers are excluded; listener-specific exceptions such as HTTP/blob sharing do not apply to invocation routing.
    /// </summary>
    private static bool IsEligible(WorkerChannel channel, string functionGroup)
        => channel is RpcClientWorkerChannel { IsReadyForInvocations: true } client
            && !string.Equals(client.FunctionGroupName, FunctionGroups.ValidationWorker, StringComparison.OrdinalIgnoreCase)
            && string.Equals(client.FunctionGroupName, functionGroup, StringComparison.OrdinalIgnoreCase);

    private sealed class GroupSelectionState
    {
        internal Lock Gate { get; } = new();

        internal int NextIndex { get; set; }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Trace, "Posting invocation {InvocationId} on worker {WorkerId}.")]
        public static partial void PostingInvocation(ILogger logger, Guid invocationId, string workerId);

        [LoggerMessage(3, LogLevel.Error, "Invocation accounting failed for invocation {InvocationId} on worker {WorkerId}.")]
        public static partial void InvocationAccountingFailed(ILogger logger, Exception exception, string invocationId, string workerId);
    }
}
