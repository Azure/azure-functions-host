// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Owns initialized client-backed worker channels, ScriptHost setup, and terminal cleanup.
/// </summary>
/// <remarks>
/// Admission and replay decisions are atomic. Different workers can link concurrently.
/// A link returns after its WorkerInit handshake and registration. Initialization additionally prepares the extension
/// source, including receiving and extracting assemblies to disk, before publishing the channel for metadata and ScriptHost
/// startup. The registry then configures it for the active ScriptHost context and retains terminal identities.
/// Only ready channels are available for invocation dispatch.
/// </remarks>
internal sealed partial class WorkerChannelRegistry :
    IWorkerChannelRegistry,
    IHostConfigurationSourceProvider,
    IRuntimeDrainWorkerManager
{
    private static readonly TimeSpan RuntimeDrainGracePeriod = TimeSpan.FromMinutes(60);

    private readonly IRpcClientWorkerChannelFactory _channelFactory;
    private readonly IConfiguration _configuration;
    private readonly IWorkerInvocationTracker _invocationTracker;
    private readonly Lock _disposeLock = new();
    private readonly IDuplexChannelFactory<StreamingMessage> _duplexChannelFactory;
    private readonly ILogger<WorkerChannelRegistry> _logger;
    private readonly WorkerExtensionPayloadManager _extensionPayloads;
    private readonly Dictionary<string, WorkerSlot> _linkAttempts = new(StringComparer.Ordinal);
    private readonly TimeSpan _linkTimeout;
    private readonly TimeSpan _runtimeDrainGracePeriod;
    private readonly bool _registryOwnedAttempt;
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<Task> _monitorTasks = [];
    private readonly CancellationTokenSource _shutdownSource = new();
    private readonly Dictionary<string, WorkerSlot> _slots = new(StringComparer.Ordinal);
    private readonly Lock _stateLock = new();
    private readonly SemaphoreSlim _httpScaleInGate = new(1, 1);
    private readonly IWorkerHostConfigurationParser _workerHostConfigurationParser;
    // Snapshot readers use only _observationLock; writers take it after _stateLock.
    private readonly Lock _observationLock = new();
    private IConfigurationSource _hostConfigurationSource;
    private bool _hostConfigurationSourceSelected;
    private IReadOnlyList<WorkerChannel> _initializedChannels = [];
    private IReadOnlyList<WorkerChannel> _linkedChannels = [];
    private ChannelSetupContext _activeSetupContext;
    private Task _disposeTask;
    private bool _disposed;
    private bool _runtimeAdmissionClosed;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _initializedChannelAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Setup writers hold _stateLock; ready-channel readers observe this published signal without taking that lock.
    private TaskCompletionSource<IReadOnlyList<WorkerChannel>> _readyChannelsAvailable = CreateReadyChannelsSignal();

    public WorkerChannelRegistry(
        IDuplexChannelFactory<StreamingMessage> duplexChannelFactory,
        IRpcClientWorkerChannelFactory channelFactory,
        ILogger<WorkerChannelRegistry> logger,
        IConfiguration configuration,
        TimeProvider timeProvider,
        IWorkerHostConfigurationParser workerHostConfigurationParser,
        IOptionsMonitor<WorkerProcessCountOptions> processOptions = null,
        IOptions<TokenServiceProxyOptions> proxyOptions = null,
        IWorkerInvocationTracker invocationTracker = null,
        WorkerExtensionPayloadManager extensionPayloads = null)
        : this(
            duplexChannelFactory,
            channelFactory,
            logger,
            configuration,
            timeProvider,
            ResolveLinkTimeout(processOptions, proxyOptions),
            workerHostConfigurationParser,
            registryOwnedAttempt: proxyOptions?.Value.Enabled == true,
            invocationTracker: invocationTracker,
            extensionPayloads: extensionPayloads)
    {
    }

    internal WorkerChannelRegistry(
        IDuplexChannelFactory<StreamingMessage> duplexChannelFactory,
        IRpcClientWorkerChannelFactory channelFactory,
        ILogger<WorkerChannelRegistry> logger,
        IConfiguration configuration,
        TimeProvider timeProvider,
        TimeSpan linkTimeout,
        IWorkerHostConfigurationParser workerHostConfigurationParser,
        bool registryOwnedAttempt = false,
        IWorkerInvocationTracker invocationTracker = null,
        TimeSpan? runtimeDrainGracePeriod = null,
        WorkerExtensionPayloadManager extensionPayloads = null)
    {
        _duplexChannelFactory = duplexChannelFactory ?? throw new ArgumentNullException(nameof(duplexChannelFactory));
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _invocationTracker = invocationTracker;
        _extensionPayloads = extensionPayloads;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(linkTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(linkTimeout.TotalMilliseconds, uint.MaxValue - 1);
        _linkTimeout = linkTimeout;
        _workerHostConfigurationParser = workerHostConfigurationParser ?? throw new ArgumentNullException(nameof(workerHostConfigurationParser));
        _registryOwnedAttempt = registryOwnedAttempt;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _runtimeDrainGracePeriod = runtimeDrainGracePeriod ?? RuntimeDrainGracePeriod;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            _runtimeDrainGracePeriod,
            TimeSpan.Zero);
    }

    private enum LinkStage
    {
        Unknown,
        ConnectingToProxy,
        CreatingWorkerChannel,
        InitializingWorker,
        ValidatingHostConfiguration,
        RegisteringChannel
    }

    internal TimeSpan LinkTimeout => _linkTimeout;

    private static TimeSpan ResolveLinkTimeout(
        IOptionsMonitor<WorkerProcessCountOptions> processOptions,
        IOptions<TokenServiceProxyOptions> proxyOptions) =>
        proxyOptions?.Value is { Enabled: true } options
            ? options.LinkTimeout
            : RpcClientWorkerChannelFactory.GetLinkTimeout(
                processOptions?.Get(RpcClientWorkerChannelFactory.WorkerOptionsName) ?? new());

    public Task<WorkerLinkResult> LinkAsync(
        string workerId,
        Uri grpcEndpoint,
        string runtimeAccessKey,
        CancellationToken cancellationToken = default)
        => LinkAsync(workerId, grpcEndpoint, runtimeAccessKey, runtimeIdentity: null, cancellationToken);

    /// <inheritdoc />
    public Task<WorkerLinkResult> LinkAsync(
        string workerId,
        Uri grpcEndpoint,
        string runtimeAccessKey,
        string runtimeIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        RpcClientFactory.ValidateEndpoint(grpcEndpoint);
        RuntimeWorkerCredentials credentials = new(
            workerId,
            runtimeAccessKey,
            _configuration,
            _timeProvider,
            _logger);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<WorkerChannel> linkCompletion;
        WorkerSlot slot;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_linkAttempts.TryGetValue(workerId, out WorkerSlot existing))
            {
                if (!string.Equals(existing.Endpoint.AbsoluteUri, grpcEndpoint.AbsoluteUri, StringComparison.Ordinal)
                    || !existing.Credentials.MatchesKey(credentials)
                    || !string.Equals(existing.RuntimeIdentity, runtimeIdentity, StringComparison.Ordinal))
                {
                    throw new WorkerLinkException(WorkerLinkFailureReason.Conflict,
                        "The worker is already associated with different endpoints, credentials, or Runtime identity.");
                }

                if (existing.Terminal || existing.Channel is { IsTerminated: true })
                {
                    throw new WorkerLinkException(WorkerLinkFailureReason.WorkerTerminated,
                        "The worker's initialized channel has terminated.");
                }

                return CompleteLinkAsync(existing.LinkTask.WaitAsync(_linkTimeout, _timeProvider, cancellationToken), isNewLink: false);
            }

            if (_runtimeAdmissionClosed)
            {
                throw new WorkerLinkException(
                    WorkerLinkFailureReason.RuntimeStopping,
                    "The Runtime has permanently closed Worker-Link admission.");
            }

            linkCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            slot = new()
            {
                Endpoint = grpcEndpoint,
                Credentials = credentials,
                RuntimeIdentity = runtimeIdentity,
                LinkTask = linkCompletion.Task,
            };
            _slots.Add(workerId, slot);
            _linkAttempts.Add(workerId, slot);
        }

        // The shared completion is published before I/O begins, and connection work runs outside the state lock.
        _ = LinkAndCompleteAsync(workerId, grpcEndpoint, slot, linkCompletion,
            _registryOwnedAttempt ? CancellationToken.None : cancellationToken);
        Task<WorkerChannel> link = linkCompletion.Task;
        if (_registryOwnedAttempt)
        {
            // Observe background failures even when all callers have stopped waiting; LinkCoreAsync logs them.
            _ = link.ContinueWith(static failed =>
                {
                    _ = failed.Exception;
                },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return CompleteLinkAsync(_registryOwnedAttempt ? link.WaitAsync(cancellationToken) : link, isNewLink: true);
    }

    private static async Task<WorkerLinkResult> CompleteLinkAsync(Task<WorkerChannel> link, bool isNewLink)
        => new(await link.ConfigureAwait(false), isNewLink);

    public int ScaleInWorkers(
        string functionGroup,
        bool isAlwaysReady,
        int desiredWorkerCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionGroup);
        ArgumentOutOfRangeException.ThrowIfNegative(desiredWorkerCount);
        if (string.Equals(functionGroup, FunctionGroups.Http, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("HTTP scale-in requires successful capacity publication before drain.", nameof(functionGroup));
        }

        List<(string WorkerId, RpcClientWorkerChannel Channel, Task InvocationDrain)> selections = [];
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            List<(string WorkerId, WorkerSlot Slot, RpcClientWorkerChannel Channel)> candidates = [];
            foreach ((string workerId, WorkerSlot slot) in _slots)
            {
                if (_activeSetupContext is null ||
                    !ReferenceEquals(slot.AttemptedSetupContext, _activeSetupContext) ||
                    slot.SetupException is not null ||
                    slot.Channel is not { } channel)
                {
                    continue;
                }

                candidates.Add(new(workerId, slot, channel));
            }

            candidates.RemoveAll(candidate => !candidate.Channel.IsScaleInCandidate(functionGroup, isAlwaysReady));
            int matchingWorkerCount = candidates.Count;

            foreach ((string workerId, WorkerSlot slot, RpcClientWorkerChannel channel) in candidates)
            {
                if (matchingWorkerCount <= desiredWorkerCount)
                {
                    break;
                }

                if (channel.TryBeginScaleIn(functionGroup, isAlwaysReady) is { } invocationDrain)
                {
                    slot.DrainStarted = true;
                    slot.InvocationDrain = invocationDrain;
                    selections.Add(new(workerId, channel, invocationDrain));
                }

                matchingWorkerCount--;
            }

            if (selections.Count > 0)
            {
                SignalReadyChannelsLocked();
                SignalChangeLocked();
            }
        }

        foreach (var selection in selections)
        {
            _ = DrainAndUnlinkAsync(
                selection.WorkerId,
                selection.Channel,
                selection.InvocationDrain,
                selectedTimestamp: null);
        }

        return selections.Count;
    }

    RuntimeDrainStart IRuntimeDrainWorkerManager.CloseAdmission()
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runtimeAdmissionClosed)
            {
                throw new InvalidOperationException("The Runtime drain admission fence is already closed.");
            }

            _runtimeAdmissionClosed = true;
            List<string> nonHttpWorkerIds = [];
            List<string> httpWorkerIds = [];
            foreach ((string workerId, WorkerSlot slot) in _slots)
            {
                if (slot.Channel is not { IsTerminated: false } channel)
                {
                    continue;
                }

                (channel.IsHttpFunctionGroup ? httpWorkerIds : nonHttpWorkerIds).Add(workerId);
            }

            nonHttpWorkerIds.Sort(StringComparer.Ordinal);
            httpWorkerIds.Sort(StringComparer.Ordinal);
            return new(nonHttpWorkerIds, httpWorkerIds);
        }
    }

    void IRuntimeDrainWorkerManager.SelectWorkers(
        IReadOnlyList<string> workerIds)
    {
        ArgumentNullException.ThrowIfNull(workerIds);

        List<(string WorkerId, RpcClientWorkerChannel Channel, Task InvocationDrain, long SelectedTimestamp)> selections = [];
        List<(string WorkerId, RpcClientWorkerChannel Channel, Task InvocationDrain, long SelectedTimestamp)> deadlineEscalations = [];
        HashSet<string> seenWorkerIds = new(StringComparer.Ordinal);
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_runtimeAdmissionClosed)
            {
                throw new InvalidOperationException("The Runtime drain admission fence is open.");
            }

            foreach (string workerId in workerIds)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
                if (!seenWorkerIds.Add(workerId) ||
                    !_slots.TryGetValue(workerId, out WorkerSlot slot) ||
                    slot.Channel is not { } channel)
                {
                    continue;
                }

                long selectedTimestamp = _timeProvider.GetTimestamp();
                Task invocationDrain = channel.TryBeginRuntimeDrain();
                if (invocationDrain is null)
                {
                    continue;
                }

                if (slot.DrainStarted)
                {
                    deadlineEscalations.Add((
                        workerId,
                        channel,
                        slot.InvocationDrain ?? invocationDrain,
                        selectedTimestamp));
                    continue;
                }

                slot.DrainStarted = true;
                slot.InvocationDrain = invocationDrain;
                selections.Add((
                    workerId,
                    channel,
                    invocationDrain,
                    selectedTimestamp));
            }

            if (selections.Count > 0)
            {
                SignalReadyChannelsLocked();
                SignalChangeLocked();
            }
        }

        foreach (var selection in selections)
        {
            _ = DrainAndUnlinkAsync(
                selection.WorkerId,
                selection.Channel,
                selection.InvocationDrain,
                selection.SelectedTimestamp);
        }

        foreach (var escalation in deadlineEscalations)
        {
            _ = EnforceRuntimeDeadlineAsync(
                escalation.WorkerId,
                escalation.Channel,
                escalation.InvocationDrain,
                escalation.SelectedTimestamp);
        }
    }

    /// <inheritdoc />
    public async Task<int> ScaleInWorkersAsync(
        string functionGroup,
        bool isAlwaysReady,
        int desiredWorkerCount,
        Func<CancellationToken, Task> publishCapacity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionGroup);
        ArgumentOutOfRangeException.ThrowIfNegative(desiredWorkerCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(functionGroup, FunctionGroups.Http, StringComparison.OrdinalIgnoreCase))
        {
            return ScaleInWorkers(functionGroup, isAlwaysReady, desiredWorkerCount);
        }

        if (!isAlwaysReady)
        {
            throw new ArgumentException("HTTP on-demand scale-in is unsupported.", nameof(isAlwaysReady));
        }

        ArgumentNullException.ThrowIfNull(publishCapacity);
        using CancellationTokenSource operation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownSource.Token);
        await _httpScaleInGate.WaitAsync(operation.Token);
        List<(string WorkerId, RpcClientWorkerChannel Channel)> reserved = [];
        List<(string WorkerId, RpcClientWorkerChannel Channel, Task InvocationDrain)> selections = [];
        try
        {
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var candidates = _slots
                    .Where(pair => _activeSetupContext is not null &&
                        ReferenceEquals(pair.Value.AttemptedSetupContext, _activeSetupContext) &&
                        pair.Value.SetupException is null &&
                        pair.Value.Channel is { } channel &&
                        channel.IsScaleInCandidate(functionGroup, isAlwaysReady))
                    .ToArray();
                int excess = Math.Max(0, candidates.Length - desiredWorkerCount);
                foreach (var candidate in candidates)
                {
                    if (reserved.Count == excess)
                    {
                        break;
                    }

                    if (candidate.Value.Channel.TryReserveHttpScaleIn())
                    {
                        reserved.Add((candidate.Key, candidate.Value.Channel));
                    }
                }

                if (reserved.Count == 0)
                {
                    return 0;
                }

                // Withdraw advertised capacity, but keep admission open until the outbound publication succeeds.
                SignalChangeLocked();
            }

            await publishCapacity(operation.Token);
            lock (_stateLock)
            {
                operation.Token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                foreach ((string workerId, RpcClientWorkerChannel channel) in reserved)
                {
                    if (_slots.TryGetValue(workerId, out WorkerSlot slot) && ReferenceEquals(slot.Channel, channel) &&
                        channel.TryBeginScaleIn(functionGroup, isAlwaysReady) is { } drainTask)
                    {
                        slot.DrainStarted = true;
                        slot.InvocationDrain = drainTask;
                        selections.Add((workerId, channel, drainTask));
                    }
                }

                SignalReadyChannelsLocked();
                SignalChangeLocked();
            }

            foreach (var selection in selections)
            {
                _ = DrainAndUnlinkAsync(
                    selection.WorkerId,
                    selection.Channel,
                    selection.InvocationDrain,
                    selectedTimestamp: null);
            }

            return selections.Count;
        }
        finally
        {
            lock (_stateLock)
            {
                foreach ((_, RpcClientWorkerChannel channel) in reserved)
                {
                    channel.ReleaseHttpScaleInReservation();
                }

                if (reserved.Count > 0)
                {
                    SignalChangeLocked();
                }
            }

            _httpScaleInGate.Release();
        }
    }

    private async Task DrainAndUnlinkAsync(
        string workerId,
        RpcClientWorkerChannel channel,
        Task invocationDrain,
        long? selectedTimestamp)
    {
        try
        {
            if (selectedTimestamp is { } timestamp)
            {
                await WaitForRuntimeDrainAsync(
                    workerId,
                    channel,
                    invocationDrain,
                    timestamp);
            }
            else
            {
                await invocationDrain.WaitAsync(_shutdownSource.Token);
            }

            if (await UnlinkAsync(workerId, _shutdownSource.Token))
            {
                Log.DrainCompleted(_logger, workerId);
            }
            else
            {
                Log.DrainAlreadyDetached(_logger, workerId);
            }
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log.DrainFailed(_logger, exception, workerId);
        }
    }

    private async Task EnforceRuntimeDeadlineAsync(
        string workerId,
        RpcClientWorkerChannel channel,
        Task invocationDrain,
        long selectedTimestamp)
    {
        try
        {
            await WaitForRuntimeDrainAsync(
                workerId,
                channel,
                invocationDrain,
                selectedTimestamp);
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log.DrainFailed(_logger, exception, workerId);
        }
    }

    private async Task WaitForRuntimeDrainAsync(
        string workerId,
        RpcClientWorkerChannel channel,
        Task invocationDrain,
        long selectedTimestamp)
    {
        using CancellationTokenSource deadlineSource =
            CancellationTokenSource.CreateLinkedTokenSource(_shutdownSource.Token);
        Task deadline = DelayFromSelectionAsync(
            selectedTimestamp,
            _runtimeDrainGracePeriod,
            deadlineSource.Token);
        Task completed = await Task.WhenAny(invocationDrain, deadline);
        if (ReferenceEquals(completed, invocationDrain))
        {
            deadlineSource.Cancel();
        }
        else
        {
            await deadline;
            channel.ExpireRuntimeDrain(
                new TimeoutException(
                    $"Worker '{workerId}' exceeded the Runtime drain grace period."));
        }

        await invocationDrain.WaitAsync(_shutdownSource.Token);
    }

    private async Task DelayFromSelectionAsync(
        long selectedTimestamp,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = delay - _timeProvider.GetElapsedTime(
            selectedTimestamp,
            _timeProvider.GetTimestamp());
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, _timeProvider, cancellationToken);
        }
    }

    private async Task LinkAndCompleteAsync(string workerId, Uri grpcEndpoint, WorkerSlot slot, TaskCompletionSource<WorkerChannel> linkCompletion,
        CancellationToken cancellationToken)
    {
        try
        {
            Log.LinkStarted(_logger, workerId);
            WorkerChannel channel = await LinkCoreAsync(workerId, grpcEndpoint, slot, cancellationToken);
            Log.LinkInitialized(_logger, workerId);
            linkCompletion.SetResult(channel);
        }
        catch (OperationCanceledException exception)
        {
            linkCompletion.SetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            linkCompletion.SetException(exception);
        }
    }

    private async Task<WorkerChannel> LinkCoreAsync(
        string workerId,
        Uri grpcEndpoint,
        WorkerSlot slot,
        CancellationToken cancellationToken)
    {
        (LinkStage Stage, string Reason, string ExceptionType)? linkFailure = null;
        try
        {
            using SemaphoreLock gate = new(slot.Gate);
            bool initialized = false;
            try
            {
                using CancellationTokenSource deadline = new(_linkTimeout, _timeProvider);
                using CancellationTokenSource operationSource =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownSource.Token, deadline.Token, slot.PendingCancellation.Token);

                DuplexChannel<StreamingMessage> ownedChannel = null;
                RpcClientWorkerChannel candidate = null;
                LinkStage stage = LinkStage.ConnectingToProxy;

                try
                {
                    operationSource.Token.ThrowIfCancellationRequested();
                    ownedChannel = slot.RuntimeIdentity is null
                        ? await _duplexChannelFactory.ConnectAsync(
                            grpcEndpoint,
                            slot.Credentials.CreateHeaders(),
                            operationSource.Token)
                        : await _duplexChannelFactory.ConnectAsync(
                            grpcEndpoint,
                            slot.Credentials.CreateHeaders(),
                            slot.RuntimeIdentity,
                            operationSource.Token);
                    operationSource.Token.ThrowIfCancellationRequested();

                    stage = LinkStage.CreatingWorkerChannel;
                    candidate = _channelFactory.Create(workerId, ownedChannel, slot.Credentials)
                        ?? throw new InvalidOperationException("The client worker channel factory returned no channel.");
                    ownedChannel = null;

                    if (!string.Equals(candidate.Id, workerId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"The client worker channel factory returned worker '{candidate.Id}' for requested worker '{workerId}'.");
                    }

                    if (_registryOwnedAttempt)
                    {
                        candidate.WorkerConfig.CountOptions.ProcessStartupTimeout = _linkTimeout;
                        candidate.WorkerConfig.CountOptions.InitializationTimeout = _linkTimeout;
                    }

                    try
                    {
                        stage = LinkStage.InitializingWorker;
                        await candidate.StartAsync(operationSource.Token);
                    }
                    catch (InvalidOperationException exception) when (candidate.Completion.IsCompleted)
                    {
                        throw new IOException("The worker channel closed before initialization completed.", exception);
                    }

                    operationSource.Token.ThrowIfCancellationRequested();
                    stage = LinkStage.ValidatingHostConfiguration;
                    IConfigurationSource configurationSource;
                    try
                    {
                        configurationSource = _workerHostConfigurationParser.Parse(candidate.HostConfiguration);
                    }
                    catch (HostConfigurationException exception)
                    {
                        throw new WorkerLinkException(WorkerLinkFailureReason.Unavailable, exception.Message);
                    }

                    stage = LinkStage.RegisteringChannel;
                    RegisterInitializedChannel(workerId, slot, candidate, configurationSource, operationSource.Token);

                    WorkerChannel linkedChannel = candidate;
                    candidate = null;
                    initialized = true;

                    return linkedChannel;
                }
                catch (Exception exception)
                {
                    (Exception failure, string reason) = exception switch
                    {
                        OperationCanceledException when cancellationToken.IsCancellationRequested
                            => (exception, "CallerCanceled"),
                        OperationCanceledException when _shutdownSource.IsCancellationRequested
                            => (new ObjectDisposedException(GetType().FullName), "HostShutdown"),
                        OperationCanceledException when deadline.IsCancellationRequested
                            => (new TimeoutException("The worker link initialization deadline expired.", exception), "InitializationDeadlineExpired"),
                        OperationCanceledException => (exception, "OperationCanceled"),
                        _ => (exception, stage switch
                        {
                            LinkStage.ConnectingToProxy => "ConnectionFailed",
                            LinkStage.CreatingWorkerChannel => "ChannelCreationFailed",
                            LinkStage.InitializingWorker => "WorkerInitializationFailed",
                            LinkStage.ValidatingHostConfiguration => "HostConfigurationValidationFailed",
                            LinkStage.RegisteringChannel => "ChannelRegistrationFailed",
                            _ => "UnexpectedFailure"
                        })
                    };
                    // Preserve the primary cause before cleanup can aggregate additional exceptions.
                    linkFailure = (stage, reason, failure.GetType().FullName);
                    failure = await candidate.DisposeAndCaptureExceptionAsync(failure);
                    failure = await ownedChannel.DisposeAndCaptureExceptionAsync(failure);
                    ExceptionDispatchInfo.Capture(failure).Throw();
                    throw;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _shutdownSource.IsCancellationRequested)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
            finally
            {
                slot.DisposePendingCancellation();
                // Accepted links are cleaned up by monitor/unlink; completing Link must not wait for deferred setup.
                if (!initialized)
                {
                    RemoveEmptySlot(workerId, slot);
                    lock (_stateLock)
                    {
                        _linkAttempts.Remove(workerId);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            var failure = linkFailure ?? (Stage: LinkStage.Unknown,
                Reason: "UnexpectedFailure", ExceptionType: exception.GetType().FullName);
            bool expectedShutdown = string.Equals(failure.Reason, "HostShutdown", StringComparison.Ordinal)
                && exception is ObjectDisposedException;
            Log.LinkEnded(_logger, expectedShutdown ? LogLevel.Information : LogLevel.Warning, workerId,
                expectedShutdown || exception is OperationCanceledException ? "Canceled" : "Failed",
                failure.Stage, failure.Reason, failure.ExceptionType);
            throw;
        }
    }

    public async Task<bool> UnlinkAsync(string workerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        cancellationToken.ThrowIfCancellationRequested();

        WorkerSlot slot;
        lock (_stateLock)
        {
            if (_disposed || !_slots.TryGetValue(workerId, out slot))
            {
                return false;
            }
        }

        bool canceledPending = _registryOwnedAttempt && slot.CancelPending();

        try
        {
            using CancellationTokenSource operationSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownSource.Token);

            SemaphoreLock gate = await slot.Gate.LockAsync(operationSource.Token);
            try
            {
                using (gate)
                {
                    operationSource.Token.ThrowIfCancellationRequested();

                    RpcClientWorkerChannel channel;
                    lock (_stateLock)
                    {
                        if (_disposed)
                        {
                            return false;
                        }

                        channel = DetachChannelLocked(slot, expectedChannel: null);
                    }

                    if (channel is null)
                    {
                        return canceledPending;
                    }

                    await channel.DisposeAsync();
                    return true;
                }
            }
            finally
            {
                RemoveEmptySlot(workerId, slot);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _shutdownSource.IsCancellationRequested)
        {
            return false;
        }
    }

    public bool TryGetInitializedChannel(string workerId, out WorkerChannel channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        lock (_stateLock)
        {
            if (!_disposed && _slots.TryGetValue(workerId, out WorkerSlot slot) && slot.Initialized && slot.Channel is not null)
            {
                channel = slot.Channel;
                return true;
            }
        }

        channel = null;
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<WorkerChannel> GetInitializedChannels()
    {
        lock (_observationLock)
        {
            return _initializedChannels;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<WorkerChannel> GetLinkedChannels()
    {
        lock (_observationLock)
        {
            return _linkedChannels;
        }
    }

    public IConfigurationSource GetConfigurationSource()
    {
        lock (_stateLock)
        {
            if (!_hostConfigurationSourceSelected)
            {
                throw new InvalidOperationException("Host configuration source has not been selected.");
            }

            return _hostConfigurationSource;
        }
    }

    /// <inheritdoc />
    public Task GetChangeTask()
    {
        lock (_observationLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _changed.Task;
        }
    }

    public Task ActivateAsync(IReadOnlyList<FunctionMetadata> functions, ManagedDependencyOptions managedDependencyOptions, TimeSpan? functionTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(managedDependencyOptions);
        cancellationToken.ThrowIfCancellationRequested();

        ChannelSetupContext setupContext = new(functions, managedDependencyOptions, functionTimeout);
        Exception firstSetupException = null;
        int attemptedChannels = 0;
        int readyChannels = 0;

        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            // Invalidate the prior ready snapshot before replacing the context, including an empty function set.
            // An incomplete signal is retained so callers already waiting are not orphaned by a ScriptHost restart.
            ResetReadyChannelsSignal();
            _activeSetupContext = setupContext;
            SignalChangeLocked();

            foreach (WorkerSlot slot in _slots.Values)
            {
                if (slot.Channel is null || !slot.Initialized)
                {
                    continue;
                }

                attemptedChannels++;
                ConfigureSlotLocked(slot);
                if (slot.SetupException is null)
                {
                    readyChannels++;
                }
                else
                {
                    firstSetupException ??= slot.SetupException;
                }
            }
        }

        if (attemptedChannels > 0 && readyChannels == 0)
        {
            // Preserve the original failure; a single healthy channel otherwise lets this ScriptHost start.
            ExceptionDispatchInfo.Capture(firstSetupException).Throw();
        }

        return Task.CompletedTask;
    }

    public ValueTask<IReadOnlyList<WorkerChannel>> GetReadyChannelsAsync()
    {
        // Retry only when another thread changes the signal before our CompareExchange succeeds; never poll for readiness.
        // Rereading preserves the newer publication. Waiting callers receive a task instead of spinning.
        while (true)
        {
            // Never wait for the setup lock. The published task is either a snapshot or the shared readiness wait.
            TaskCompletionSource<IReadOnlyList<WorkerChannel>> signal = GetReadyChannelsSignal();
            if (!signal.Task.IsCompletedSuccessfully)
            {
                return new(signal.Task);
            }

            IReadOnlyList<WorkerChannel> readyChannels = signal.Task.Result;
            if (readyChannels.Any(channel => channel is not RpcClientWorkerChannel { IsReadyForInvocations: true }))
            {
                // Transport completion can precede registry cleanup, so exclude channels that stopped since publication.
                readyChannels = [.. readyChannels.Where(channel => channel is RpcClientWorkerChannel { IsReadyForInvocations: true })];
            }

            if (readyChannels.Count > 0)
            {
                return new(readyChannels);
            }

            // Replace a stale completed signal only if a setup/cleanup writer has not published a newer snapshot.
            TaskCompletionSource<IReadOnlyList<WorkerChannel>> replacement = CreateReadyChannelsSignal();
            TaskCompletionSource<IReadOnlyList<WorkerChannel>> observedSignal =
                Interlocked.CompareExchange(ref _readyChannelsAvailable, replacement, signal);

            if (!ReferenceEquals(observedSignal, signal))
            {
                // Another thread changed the signal; retry against the newer publication.
                continue;
            }

            return new(replacement.Task);
        }
    }

    public async Task<WorkerChannel> WaitForFirstInitializedAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task available;
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                RpcClientWorkerChannel channel = _slots.Values
                    .Where(slot => slot.Initialized && slot.Channel is { IsTerminated: false })
                    .Select(slot => slot.Channel)
                    .OrderBy(channel => channel.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (channel is not null)
                {
                    return channel;
                }

                if (_initializedChannelAvailable.Task.IsCompleted)
                {
                    _initializedChannelAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                available = _initializedChannelAvailable.Task;
            }

            await available.WaitAsync(cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            _disposeTask ??= DisposeAsyncCore();
            return new(_disposeTask);
        }
    }

    private async Task DisposeAsyncCore()
    {
        KeyValuePair<string, WorkerSlot>[] slots;
        Task[] monitorTasks;

        lock (_stateLock)
        {
            lock (_observationLock)
            {
                _disposed = true;
                _initializedChannels = [];
                _linkedChannels = [];
                _changed.TrySetResult();
            }

            slots = [.. _slots];
            monitorTasks = [.. _monitorTasks];
            _initializedChannelAvailable.TrySetResult();

            // Publish disposal for new readers and release callers awaiting the previous shared signal.
            ObjectDisposedException exception = new(GetType().FullName);
            TaskCompletionSource<IReadOnlyList<WorkerChannel>> disposedSignal = CreateReadyChannelsSignal();
            disposedSignal.SetException(exception);
            Interlocked.Exchange(ref _readyChannelsAvailable, disposedSignal).TrySetException(exception);
        }

        Exception disposalException = null;
        try
        {
            await _shutdownSource.CancelAsync();
        }
        catch (Exception exception)
        {
            disposalException = exception;
        }

        Task<Exception>[] slotDisposals = [.. slots.Select(slot => ShutdownSlotAsync(slot.Key, slot.Value))];
        Exception[] slotExceptions = await Task.WhenAll(slotDisposals);
        foreach (Exception exception in slotExceptions)
        {
            if (exception is not null)
            {
                disposalException = AggregateException.Combine(disposalException, exception);
            }
        }

        await Task.WhenAll(monitorTasks);

        if (disposalException is not null)
        {
            ExceptionDispatchInfo.Capture(disposalException).Throw();
        }
    }

    private RpcClientWorkerChannel DetachChannelLocked(WorkerSlot slot, RpcClientWorkerChannel expectedChannel)
    {
        RpcClientWorkerChannel channel = slot.Channel;
        if (channel is null || (expectedChannel is not null && !ReferenceEquals(channel, expectedChannel)))
        {
            return null;
        }

        slot.Channel = null;
        slot.Initialized = false;
        slot.AttemptedSetupContext = null;
        slot.SetupException = null;
        _invocationTracker?.WorkerUnlinked(channel.Id);
        if (slot.LinkTask is not null)
        {
            slot.Terminal = true;
            slot.LinkTask = null;
        }

        if (!_disposed)
        {
            // Remove the detached channel from the published snapshot before channel disposal begins.
            SignalReadyChannelsLocked();
            PublishInitializedChannelsLocked();
            if (_initializedChannelAvailable.Task.IsCompleted &&
                !_slots.Values.Any(slot => slot.Initialized && slot.Channel is { IsTerminated: false }))
            {
                _initializedChannelAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        return channel;
    }

    private void RemoveEmptySlot(string workerId, WorkerSlot slot)
    {
        lock (_stateLock)
        {
            if (slot.Channel is null &&
                _slots.TryGetValue(workerId, out WorkerSlot currentSlot) && ReferenceEquals(currentSlot, slot))
            {
                _slots.Remove(workerId);
            }
        }
    }

    private async Task MonitorChannelCompletionAsync(string workerId, WorkerSlot slot, RpcClientWorkerChannel channel)
    {
        Exception terminalException = null;
        try
        {
            // Client workers are external; transport completion and fatal protocol failures end their channel lifetime.
            await Task.WhenAny(channel.Completion, channel.FunctionLoadFailure).WaitAsync(_shutdownSource.Token);
            if (channel.FunctionLoadFailure.IsCompletedSuccessfully)
            {
                terminalException = channel.FunctionLoadFailure.Result;
            }
            else
            {
                await channel.Completion;
            }
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            terminalException = exception;
        }

        SemaphoreLock gate = await slot.Gate.LockAsync();
        try
        {
            using (gate)
            {
                // LinkAsync holds this gate until the monitor is tracked and publication completes. It also serializes this
                // cleanup with unlink and shutdown.
                RpcClientWorkerChannel removedChannel;
                lock (_stateLock)
                {
                    // Only detach the channel associated with this completion signal.
                    removedChannel = DetachChannelLocked(slot, channel);
                }

                if (removedChannel is null)
                {
                    return;
                }

                if (terminalException is null)
                {
                    Log.ChannelCompleted(_logger, workerId);
                }
                else
                {
                    Log.ChannelFailed(_logger, terminalException, workerId);
                }

                try
                {
                    removedChannel.Shutdown(terminalException);
                }
                catch (Exception exception)
                {
                    Log.ChannelShutdownFailed(_logger, exception, workerId);
                }

                try
                {
                    await removedChannel.DisposeAsync();
                }
                catch (Exception exception)
                {
                    Log.ChannelDisposalFailed(_logger, exception, workerId);
                }
            }
        }
        catch (Exception exception)
        {
            Log.ChannelRemovalFailed(_logger, exception, workerId);
        }
        finally
        {
            RemoveEmptySlot(workerId, slot);
        }
    }

    private void OnMonitorCompleted(Task monitorTask)
    {
        lock (_stateLock)
        {
            _monitorTasks.Remove(monitorTask);
        }
    }

    private void RegisterInitializedChannel(
        string workerId,
        WorkerSlot slot,
        RpcClientWorkerChannel channel,
        IConfigurationSource configurationSource,
        CancellationToken cancellationToken)
    {
        Task monitorTask;
        Task preparationTask = null;

        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runtimeAdmissionClosed)
            {
                throw new WorkerLinkException(
                    WorkerLinkFailureReason.RuntimeStopping,
                    "The Runtime closed Worker-Link admission while this link was initializing.");
            }

            // Setup can hold this lock past the link deadline or transport lifetime.
            cancellationToken.ThrowIfCancellationRequested();
            if (!_slots.TryGetValue(workerId, out WorkerSlot currentSlot) || !ReferenceEquals(currentSlot, slot) ||
                slot.Terminal || slot.Channel is not null)
            {
                throw new WorkerLinkException(WorkerLinkFailureReason.Conflict, "The worker link is no longer available for admission.");
            }

            if (channel.IsTerminated)
            {
                Exception failure = channel.FunctionLoadFailure.IsCompletedSuccessfully
                    ? channel.FunctionLoadFailure.Result
                    : channel.Completion.Exception?.GetBaseException();
                throw new IOException("The worker channel terminated before initialization was registered.", failure);
            }

            // Retain the first configuration source or file-based loading choice for the root lifetime.
            if (!_hostConfigurationSourceSelected)
            {
                _hostConfigurationSource = configurationSource;
                _hostConfigurationSourceSelected = true;
            }

            _invocationTracker?.WorkerLinked(workerId);
            slot.Channel = channel;
            slot.Initialized = _extensionPayloads is null;
            monitorTask = MonitorChannelCompletionAsync(workerId, slot, channel);
            _monitorTasks.Add(monitorTask);
            if (slot.Initialized)
            {
                _initializedChannelAvailable.TrySetResult();
            }
            else
            {
                preparationTask = PrepareExtensionPayloadAsync(slot, channel);
                _monitorTasks.Add(preparationTask);
            }
            PublishInitializedChannelsLocked();
        }

        _ = ConfigureSlotInBackgroundAsync(slot);
        _ = monitorTask.ContinueWith(static (task, state) => ((WorkerChannelRegistry)state).OnMonitorCompleted(task), this,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        if (preparationTask is not null)
        {
            _ = preparationTask.ContinueWith(static (task, state) => ((WorkerChannelRegistry)state).OnMonitorCompleted(task), this,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private async Task PrepareExtensionPayloadAsync(WorkerSlot slot, RpcClientWorkerChannel channel)
    {
        // Link admission ends at WorkerInit. This work belongs to the root/link lifetime, not the HTTP request.
        await Task.Yield();
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(_shutdownSource.Token);
        try
        {
            Task preparation = _extensionPayloads.PrepareAsync(channel, lifetime.Token);
            if (!ReferenceEquals(await Task.WhenAny(preparation, channel.Completion), preparation))
            {
                await lifetime.CancelAsync();
            }

            await preparation;
            lock (_stateLock)
            {
                if (_disposed || !ReferenceEquals(slot.Channel, channel) || channel.IsTerminated)
                {
                    return;
                }

                slot.Initialized = true;
                _initializedChannelAvailable.TrySetResult();
                PublishInitializedChannelsLocked();
                ConfigureSlotLocked(slot);
            }
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
        {
            // Root shutdown owns channel disposal and waits for this preparation to release its staging files.
        }
        catch (Exception exception)
        {
            Log.ExtensionPayloadRejected(_logger, exception, channel.Id);
            try
            {
                await UnlinkAsync(channel.Id);
            }
            catch (Exception cleanupException)
            {
                Log.ChannelDisposalFailed(_logger, new AggregateException(exception, cleanupException), channel.Id);
            }
        }
    }

    private async Task ConfigureSlotInBackgroundAsync(WorkerSlot slot)
    {
        // Keep function setup out of the WorkerInit/link call path.
        await Task.Yield();

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            ConfigureSlotLocked(slot);
        }
    }

    private void ConfigureSlotLocked(WorkerSlot slot)
    {
        // Read the context when deferred setup runs, not when the channel linked, to handle ScriptHost restarts.
        ChannelSetupContext setupContext = _activeSetupContext;
        RpcClientWorkerChannel channel = slot.Channel;
        if (setupContext is null ||
            channel is null ||
            !slot.Initialized ||
            ReferenceEquals(slot.AttemptedSetupContext, setupContext))
        {
            return;
        }

        // Each slot gets one attempt per context. Failed setup stays excluded until a new activation retries it.
        slot.AttemptedSetupContext = setupContext;
        slot.SetupException = null;
        try
        {
            channel.SetupFunctionInvocationBuffers(setupContext.Functions);
            channel.SendFunctionLoadRequests(setupContext.ManagedDependencyOptions, setupContext.FunctionTimeout);
            SignalReadyChannelsLocked();
        }
        catch (Exception exception)
        {
            slot.SetupException = exception;
            Log.ChannelSetupFailed(_logger, exception, channel.Id);
        }
        finally
        {
            SignalChangeLocked();
        }
    }

    private IReadOnlyList<WorkerChannel> GetReadyChannelsLocked()
    {
        ChannelSetupContext setupContext = _activeSetupContext;
        if (setupContext is null)
        {
            return [];
        }

        // Invocation buffers alone are insufficient: sending load requests must also succeed for this context.
        return [.. _slots.Values
            .Where(slot =>
                ReferenceEquals(slot.AttemptedSetupContext, setupContext) &&
                slot.Initialized &&
                slot.SetupException is null &&
                slot.Channel is { IsReadyForInvocations: true })
            .Select(slot => (WorkerChannel)slot.Channel)];
    }

    private void SignalReadyChannelsLocked()
    {
        IReadOnlyList<WorkerChannel> readyChannels = GetReadyChannelsLocked();
        if (readyChannels.Count == 0)
        {
            ResetReadyChannelsSignal();
            return;
        }

        // Publish the current snapshot for new readers, then complete the shared wait with the same snapshot.
        TaskCompletionSource<IReadOnlyList<WorkerChannel>> readySignal = CreateReadyChannelsSignal();
        readySignal.SetResult(readyChannels);
        Interlocked.Exchange(ref _readyChannelsAvailable, readySignal).TrySetResult(readyChannels);
    }

    private void ResetReadyChannelsSignal()
    {
        while (true)
        {
            TaskCompletionSource<IReadOnlyList<WorkerChannel>> signal = GetReadyChannelsSignal();
            if (!signal.Task.IsCompleted)
            {
                return;
            }

            // Readers can concurrently replace stale signals; preserve whichever incomplete wait wins.
            TaskCompletionSource<IReadOnlyList<WorkerChannel>> replacement = CreateReadyChannelsSignal();
            if (ReferenceEquals(Interlocked.CompareExchange(ref _readyChannelsAvailable, replacement, signal), signal))
            {
                return;
            }
        }
    }

    private TaskCompletionSource<IReadOnlyList<WorkerChannel>> GetReadyChannelsSignal()
        => Interlocked.CompareExchange(ref _readyChannelsAvailable, null, null);

    private static TaskCompletionSource<IReadOnlyList<WorkerChannel>> CreateReadyChannelsSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void SignalChangeLocked()
    {
        lock (_observationLock)
        {
            CompleteChangeLocked();
        }
    }

    private void PublishInitializedChannelsLocked()
    {
        IReadOnlyList<WorkerChannel> channels = Array.AsReadOnly<WorkerChannel>(
            [.. _slots.Values.Where(slot => slot.Initialized && slot.Channel is not null).Select(slot => (WorkerChannel)slot.Channel)]);
        IReadOnlyList<WorkerChannel> linkedChannels = Array.AsReadOnly<WorkerChannel>(
            [.. _slots.Values.Where(slot => slot.Channel is not null).Select(slot => (WorkerChannel)slot.Channel)]);
        lock (_observationLock)
        {
            _initializedChannels = channels;
            _linkedChannels = linkedChannels;
            CompleteChangeLocked();
        }
    }

    // Requires _observationLock so membership publication and invalidation are observed atomically.
    private void CompleteChangeLocked()
    {
        TaskCompletionSource changed = _changed;
        _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult();
    }

    private async Task<Exception> ShutdownSlotAsync(string workerId, WorkerSlot slot)
    {
        Exception disposalException = null;

        SemaphoreLock gate = await slot.Gate.LockAsync();
        try
        {
            using (gate)
            {
                RpcClientWorkerChannel channel;
                lock (_stateLock)
                {
                    channel = DetachChannelLocked(slot, expectedChannel: null);
                }

                disposalException = await channel.DisposeAndCaptureExceptionAsync(disposalException);
            }
        }
        catch (Exception exception)
        {
            disposalException = AggregateException.Combine(disposalException, exception);
        }
        finally
        {
            RemoveEmptySlot(workerId, slot);
        }

        return disposalException;
    }

    private sealed class WorkerSlot
    {
        private readonly Lock _pendingLock = new();
        private bool _pendingDisposed;

        public CancellationTokenSource PendingCancellation { get; } = new();

        // The exact active setup context attempted for this channel.
        public ChannelSetupContext AttemptedSetupContext { get; set; }

        public RpcClientWorkerChannel Channel { get; set; }

        public bool Initialized { get; set; }

        // The initial link owns this gate before its slot is published.
        public SemaphoreSlim Gate { get; } = new(0, 1);

        // The recorded gRPC endpoint distinguishes exact retries from conflicting requests.
        public Uri Endpoint { get; init; }

        public RuntimeWorkerCredentials Credentials { get; init; }

        public string RuntimeIdentity { get; init; }

        // The link task shared by exact retries; cleared when the accepted channel is detached.
        public Task<WorkerChannel> LinkTask { get; set; }

        // The original failure from the attempted setup context, or null after successful setup.
        public Exception SetupException { get; set; }

        public bool DrainStarted { get; set; }

        public Task InvocationDrain { get; set; }

        // Indicates that a previously accepted link has terminated and cannot be replayed.
        public bool Terminal { get; set; }

        public bool CancelPending()
        {
            lock (_pendingLock)
            {
                if (!_pendingDisposed)
                {
                    PendingCancellation.Cancel();
                    return true;
                }

                return false;
            }
        }

        public void DisposePendingCancellation()
        {
            lock (_pendingLock)
            {
                _pendingDisposed = true;
                PendingCancellation.Dispose();
            }
        }
    }

    private sealed record ChannelSetupContext(
        IReadOnlyList<FunctionMetadata> Functions,
        ManagedDependencyOptions ManagedDependencyOptions,
        TimeSpan? FunctionTimeout);

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Debug, "FunctionRpc channel for worker {WorkerId} completed and was removed.")]
        public static partial void ChannelCompleted(ILogger logger, string workerId);

        [LoggerMessage(1, LogLevel.Warning, "FunctionRpc channel for worker {WorkerId} failed and was removed.")]
        public static partial void ChannelFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(2, LogLevel.Error, "Failed to dispose the completed FunctionRpc channel for worker {WorkerId}.")]
        public static partial void ChannelDisposalFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(3, LogLevel.Error, "Failed to remove the completed FunctionRpc channel for worker {WorkerId}.")]
        public static partial void ChannelRemovalFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(4, LogLevel.Error, "Failed to shut down the completed FunctionRpc channel for worker {WorkerId}.")]
        public static partial void ChannelShutdownFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(5, LogLevel.Warning, "Failed to configure client-backed worker {WorkerId} for the active ScriptHost.")]
        public static partial void ChannelSetupFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(9, LogLevel.Information, "Worker {WorkerId} completed drain and closed its Runtime link.")]
        public static partial void DrainCompleted(ILogger logger, string workerId);

        [LoggerMessage(10, LogLevel.Error, "Worker {WorkerId} failed while completing drain.")]
        public static partial void DrainFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(11, LogLevel.Information,
            "Worker {WorkerId} completed drain, but its Runtime link was already detached or shutdown prevented a new unlink.")]
        public static partial void DrainAlreadyDetached(ILogger logger, string workerId);

        [LoggerMessage(6, LogLevel.Information, "FunctionRpc link for worker {WorkerId} started.")]
        public static partial void LinkStarted(ILogger logger, string workerId);

        [LoggerMessage(7, LogLevel.Information, "FunctionRpc link for worker {WorkerId} initialized.")]
        public static partial void LinkInitialized(ILogger logger, string workerId);

        [LoggerMessage(EventId = 8,
            Message = "FunctionRpc link for worker {WorkerId} ended with {Outcome} during {Stage}. Reason: {Reason}. Exception type: {ExceptionType}.")]
        public static partial void LinkEnded(ILogger logger, LogLevel level, string workerId, string outcome,
            LinkStage stage, string reason, string exceptionType);

        [LoggerMessage(12, LogLevel.Warning, "The extension payload for worker {WorkerId} was rejected; its link will be removed.")]
        public static partial void ExtensionPayloadRejected(ILogger logger, Exception exception, string workerId);
    }
}
