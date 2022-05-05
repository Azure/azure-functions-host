﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    internal class RpcFunctionInvocationDispatcher : IFunctionInvocationDispatcher
    {
        private readonly IMetricsLogger _metricsLogger;
        private readonly ILogger _logger;
        private readonly IRpcWorkerChannelFactory _rpcWorkerChannelFactory;
        private readonly IEnvironment _environment;
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly SemaphoreSlim _startWorkerProcessLock = new SemaphoreSlim(1, 1);
        private readonly TimeSpan _thresholdBetweenRestarts = TimeSpan.FromMinutes(WorkerConstants.WorkerRestartErrorIntervalThresholdInMinutes);
        private readonly IOptions<WorkerConcurrencyOptions> _workerConcurrencyOptions;
        private readonly IEnumerable<RpcWorkerConfig> _workerConfigs;
        private readonly Lazy<Task<int>> _maxProcessCount;

        private IScriptEventManager _eventManager;
        private IWebHostRpcWorkerChannelManager _webHostLanguageWorkerChannelManager;
        private IJobHostRpcWorkerChannelManager _jobHostLanguageWorkerChannelManager;
        private IDisposable _workerErrorSubscription;
        private IDisposable _workerRestartSubscription;
        private ScriptJobHostOptions _scriptOptions;
        private IRpcFunctionInvocationDispatcherLoadBalancer _functionDispatcherLoadBalancer;
        private bool _disposed = false;
        private bool _disposing = false;
        private IOptions<ManagedDependencyOptions> _managedDependencyOptions;
        private string _workerRuntime;
        private Action _shutdownStandbyWorkerChannels;
        private IEnumerable<FunctionMetadata> _functions;
        private ConcurrentStack<WorkerErrorEvent> _languageWorkerErrors = new ConcurrentStack<WorkerErrorEvent>();
        private CancellationTokenSource _processStartCancellationToken = new CancellationTokenSource();
        private CancellationTokenSource _disposeToken = new CancellationTokenSource();
        private TimeSpan _processStartupInterval;
        private TimeSpan _restartWait;
        private TimeSpan _shutdownTimeout;
        private bool _workerIndexing;

        public RpcFunctionInvocationDispatcher(IOptions<ScriptJobHostOptions> scriptHostOptions,
            IMetricsLogger metricsLogger,
            IEnvironment environment,
            IApplicationLifetime applicationLifetime,
            IScriptEventManager eventManager,
            ILoggerFactory loggerFactory,
            IRpcWorkerChannelFactory rpcWorkerChannelFactory,
            IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions,
            IWebHostRpcWorkerChannelManager webHostLanguageWorkerChannelManager,
            IJobHostRpcWorkerChannelManager jobHostLanguageWorkerChannelManager,
            IOptions<ManagedDependencyOptions> managedDependencyOptions,
            IRpcFunctionInvocationDispatcherLoadBalancer functionDispatcherLoadBalancer,
            IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions)
        {
            _metricsLogger = metricsLogger;
            _scriptOptions = scriptHostOptions.Value;
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _applicationLifetime = applicationLifetime;
            _webHostLanguageWorkerChannelManager = webHostLanguageWorkerChannelManager;
            _jobHostLanguageWorkerChannelManager = jobHostLanguageWorkerChannelManager;
            _eventManager = eventManager;
            _workerConfigs = languageWorkerOptions?.CurrentValue?.WorkerConfigs ?? throw new ArgumentNullException(nameof(languageWorkerOptions));
            _managedDependencyOptions = managedDependencyOptions ?? throw new ArgumentNullException(nameof(managedDependencyOptions));
            _logger = loggerFactory.CreateLogger<RpcFunctionInvocationDispatcher>();
            _rpcWorkerChannelFactory = rpcWorkerChannelFactory;
            _workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);
            _functionDispatcherLoadBalancer = functionDispatcherLoadBalancer;
            _workerConcurrencyOptions = workerConcurrencyOptions;
            _workerIndexing = Utility.CanWorkerIndex(_workerConfigs, _environment);
            State = FunctionInvocationDispatcherState.Default;

            _workerErrorSubscription = _eventManager.OfType<WorkerErrorEvent>().Subscribe(WorkerError);
            _workerRestartSubscription = _eventManager.OfType<WorkerRestartEvent>().Subscribe(WorkerRestart);

            _shutdownStandbyWorkerChannels = ShutdownWebhostLanguageWorkerChannels;
            _shutdownStandbyWorkerChannels = _shutdownStandbyWorkerChannels.Debounce(milliseconds: 5000);

            _maxProcessCount = new Lazy<Task<int>>(GetMaxProcessCount);
        }

        internal Task<int> MaxProcessCount => _maxProcessCount.Value;

        public FunctionInvocationDispatcherState State { get; private set; }

        public int ErrorEventsThreshold { get; private set; }

        public IJobHostRpcWorkerChannelManager JobHostLanguageWorkerChannelManager => _jobHostLanguageWorkerChannelManager;

        internal ConcurrentStack<WorkerErrorEvent> LanguageWorkerErrors => _languageWorkerErrors;

        internal IWebHostRpcWorkerChannelManager WebHostLanguageWorkerChannelManager => _webHostLanguageWorkerChannelManager;

        private async Task<int> GetMaxProcessCount()
        {
            if (_workerConcurrencyOptions != null && !string.IsNullOrEmpty(_workerRuntime))
            {
                var workerConfig = _workerConfigs.Where(c => c.Description.Language.Equals(_workerRuntime, StringComparison.InvariantCultureIgnoreCase))
                                                 .FirstOrDefault();
                if (workerConfig != null)
                {
                    return _environment.IsWorkerDynamicConcurrencyEnabled() ? _workerConcurrencyOptions.Value.MaxWorkerCount : workerConfig.CountOptions.ProcessCount;
                }
            }

            return (await GetAllWorkerChannelsAsync()).Count();
        }

        internal async Task InitializeJobhostLanguageWorkerChannelAsync()
        {
            await InitializeJobhostLanguageWorkerChannelAsync(0);
        }

        internal async Task InitializeJobhostLanguageWorkerChannelAsync(int attemptCount)
        {
            var rpcWorkerChannel = _rpcWorkerChannelFactory.Create(_scriptOptions.RootScriptPath, _workerRuntime, _metricsLogger, attemptCount, _workerConfigs);
            _jobHostLanguageWorkerChannelManager.AddChannel(rpcWorkerChannel);
            await rpcWorkerChannel.StartWorkerProcessAsync();
            _logger.LogDebug("Adding jobhost language worker channel for runtime: {language}. workerId:{id}", _workerRuntime, rpcWorkerChannel.Id);

            // if the worker is indexing, we will not have function metadata yet so we cannot perform these next three lines
            if (!_workerIndexing)
            {
                rpcWorkerChannel.SetupFunctionInvocationBuffers(_functions);
                rpcWorkerChannel.SendFunctionLoadRequests(_managedDependencyOptions.Value, _scriptOptions.FunctionTimeout);
                SetFunctionDispatcherStateToInitializedAndLog();
            }
        }

        private void SetFunctionDispatcherStateToInitializedAndLog()
        {
            State = FunctionInvocationDispatcherState.Initialized;
            // Do not change this log message. Vs Code relies on this to figure out when to attach debugger to the worker process.
            _logger.LogInformation("Worker process started and initialized.");
        }

        internal async Task InitializeWebhostLanguageWorkerChannel()
        {
            _logger.LogDebug("Creating new webhost language worker channel for runtime:{workerRuntime}.", _workerRuntime);
            IRpcWorkerChannel workerChannel = await _webHostLanguageWorkerChannelManager.InitializeChannelAsync(_workerRuntime);

            // if the worker is indexing, we will not have function metadata yet so we cannot perform the next two lines
            if (!_workerIndexing)
            {
                workerChannel.SetupFunctionInvocationBuffers(_functions);
                workerChannel.SendFunctionLoadRequests(_managedDependencyOptions.Value, _scriptOptions.FunctionTimeout);
                SetFunctionDispatcherStateToInitializedAndLog();
            }
        }

        internal async void ShutdownWebhostLanguageWorkerChannels()
        {
            _logger.LogDebug("{workerRuntimeConstant}={value}. Will shutdown all the worker channels that started in placeholder mode", RpcWorkerConstants.FunctionWorkerRuntimeSettingName, _workerRuntime);
            await _webHostLanguageWorkerChannelManager?.ShutdownChannelsAsync();
        }

        private void SetDispatcherStateToInitialized(Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> webhostLanguageWorkerChannel = null)
        {
            // RanToCompletion indicates successful process startup
            if (State != FunctionInvocationDispatcherState.Initialized
                && webhostLanguageWorkerChannel != null
                && webhostLanguageWorkerChannel.Where(a => a.Value.Task.Status == TaskStatus.RanToCompletion).Any())
            {
                SetFunctionDispatcherStateToInitializedAndLog();
            }
        }

        private void StartWorkerProcesses(int startIndex, Func<Task> startAction, bool initializeDispatcher = false, Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> webhostLanguageWorkerChannel = null)
        {
            Task.Run(async () =>
            {
                for (var count = startIndex; count < (await _maxProcessCount.Value)
                    && !_processStartCancellationToken.IsCancellationRequested; count++)
                {
                    if (_environment.IsWorkerDynamicConcurrencyEnabled() && count > 0)
                    {
                        // Make sure only one worker is started if concurrency is enabled
                        break;
                    }
                    try
                    {
                        await startAction();

                        // It is necessary that webhostLanguageWorkerChannel.Any() happens in this thread since 'startAction()' above modifies this collection.
                        if (initializeDispatcher)
                        {
                            SetDispatcherStateToInitialized(webhostLanguageWorkerChannel);
                        }

                        await Task.Delay(_processStartupInterval);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to start a new language worker for runtime: {_workerRuntime}.");
                    }
                }

                // It is necessary that webhostLanguageWorkerChannel.Any() happens in this thread since 'startAction()' above can modify this collection.
                // WebhostLanguageWorkerChannel can be initialized and process started up outside of StartWorkerProcesses as well, hence the statement here.
                if (initializeDispatcher)
                {
                    SetDispatcherStateToInitialized(webhostLanguageWorkerChannel);
                }
            }, _processStartCancellationToken.Token);
        }

        public async Task InitializeAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var placeholderModeEnabled = _environment.IsPlaceholderModeEnabled();
            _logger.LogDebug($"Placeholder mode is enabled: {placeholderModeEnabled}");

            if (placeholderModeEnabled)
            {
                return;
            }

            _workerRuntime = _workerRuntime ?? Utility.GetWorkerRuntime(functions, _environment);

            if (string.IsNullOrEmpty(_workerRuntime) || _workerRuntime.Equals(RpcWorkerConstants.DotNetLanguageWorkerName, StringComparison.InvariantCultureIgnoreCase))
            {
                // Shutdown any placeholder channels for empty function apps or dotnet function apps.
                // This is needed as specilization does not kill standby placeholder channels if worker runtime is not set.
                // Debouce to ensure this does not effect cold start
                _shutdownStandbyWorkerChannels();
                return;
            }

            var workerConfig = _workerConfigs.Where(c => c.Description.Language.Equals(_workerRuntime, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
            if (workerConfig == null && (functions == null || functions.Count() == 0))
            {
                // Only throw if workerConfig is null AND some functions have been found.
                // With .NET out-of-proc, worker config comes from functions.
                var allLanguageNamesFromWorkerConfigs = string.Join(",", _workerConfigs.Select(c => c.Description.Language));
                _logger.LogDebug($"Languages present in WorkerConfig: {allLanguageNamesFromWorkerConfigs}");

                throw new InvalidOperationException($"WorkerConfig for runtime: {_workerRuntime} not found");
            }

            if ((functions == null || functions.Count() == 0) && !_workerIndexing)
            {
                // do not initialize function dispatcher if there are no functions, unless the worker is indexing
                _logger.LogDebug($"{nameof(RpcFunctionInvocationDispatcher)} received no functions");
                return;
            }

            _functions = functions ?? new List<FunctionMetadata>();
            _processStartupInterval = workerConfig.CountOptions.ProcessStartupInterval;
            _restartWait = workerConfig.CountOptions.ProcessRestartInterval;
            _shutdownTimeout = workerConfig.CountOptions.ProcessShutdownTimeout;
            ErrorEventsThreshold = 3 * (await _maxProcessCount.Value);

            if (Utility.IsSupportedRuntime(_workerRuntime, _workerConfigs))
            {
                State = FunctionInvocationDispatcherState.Initializing;
                Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> webhostLanguageWorkerChannels = _webHostLanguageWorkerChannelManager.GetChannels(_workerRuntime);
                if (webhostLanguageWorkerChannels != null)
                {
                    int countOfReadyChannels = 0;
                    foreach (string workerId in webhostLanguageWorkerChannels.Keys.ToList())
                    {
                        if (webhostLanguageWorkerChannels.TryGetValue(workerId, out TaskCompletionSource<IRpcWorkerChannel> initializedLanguageWorkerChannelTask))
                        {
                            _logger.LogDebug("Found initialized language worker channel for runtime: {workerRuntime} workerId:{workerId}", _workerRuntime, workerId);
                            try
                            {
                                IRpcWorkerChannel initializedLanguageWorkerChannel = await initializedLanguageWorkerChannelTask.Task;

                                // if worker is not indexing, then _functions is populated and we can set up invocation buffers and send load requests
                                if (!_workerIndexing)
                                {
                                    initializedLanguageWorkerChannel.SetupFunctionInvocationBuffers(_functions);
                                    initializedLanguageWorkerChannel.SendFunctionLoadRequests(_managedDependencyOptions.Value, _scriptOptions.FunctionTimeout);
                                    ++countOfReadyChannels;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Removing errored webhost language worker channel for runtime: {workerRuntime} workerId:{workerId}", _workerRuntime, workerId);
                                await _webHostLanguageWorkerChannelManager.ShutdownChannelIfExistsAsync(_workerRuntime, workerId, ex);
                            }
                        }
                    }
                    StartWorkerProcesses(countOfReadyChannels, InitializeWebhostLanguageWorkerChannel, true, _webHostLanguageWorkerChannelManager.GetChannels(_workerRuntime));
                }
                else
                {
                    // if _workerIndexing, initialize a single channel and let the rest start up the background
                    if (_workerIndexing)
                    {
                        await InitializeJobhostLanguageWorkerChannelAsync();
                        StartWorkerProcesses(1, InitializeJobhostLanguageWorkerChannelAsync);
                    }
                    else
                    {
                        StartWorkerProcesses(0, InitializeJobhostLanguageWorkerChannelAsync);
                    }
                }
            }
        }

        // Gets metadata from worker
        public async Task<IEnumerable<RawFunctionMetadata>> GetWorkerMetadata()
        {
            // calling GetAllWorkerChannelsAsync() instead of GetInitializedWorkerChannelsAsync() as invocation buffers are not setup yet
            var channels = (await GetAllWorkerChannelsAsync()).ToArray();
            return (channels != null && channels.Length > 0) ? await channels.First().GetFunctionMetadata() : null;
        }

        // Second part of split InitializeAsync - can only be done after the host receives function metadata from worker
        public async Task FinishInitialization(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_environment.IsPlaceholderModeEnabled())
            {
                return;
            }

            _functions = functions;

            if (functions == null || functions.Count() == 0)
            {
                // do not setup invocation buffers or send load requests if there are no valid functions
                _logger.LogDebug("RpcFunctionInvocationDispatcher received no functions from WorkerFunctionMetadatProvider.");
                return;
            }

            if (Utility.IsSupportedRuntime(_workerRuntime, _workerConfigs))
            {
                IEnumerable<IRpcWorkerChannel> channels = await GetAllWorkerChannelsAsync();
                foreach (IRpcWorkerChannel initializedLanguageWorkerChannel in channels)
                {
                    initializedLanguageWorkerChannel.SetupFunctionInvocationBuffers(_functions);
                    initializedLanguageWorkerChannel.SendFunctionLoadRequests(_managedDependencyOptions.Value, _scriptOptions.FunctionTimeout);
                }
                SetFunctionDispatcherStateToInitializedAndLog();
            }
        }

        public async Task<IDictionary<string, WorkerStatus>> GetWorkerStatusesAsync()
        {
            var workerChannels = (await GetInitializedWorkerChannelsAsync()).ToArray();
            _logger.LogDebug($"[HostMonitor] Checking worker statuses (Count={workerChannels.Length})");

            // invoke the requests to each channel in parallel
            var workerStatuses = new Dictionary<string, WorkerStatus>(StringComparer.OrdinalIgnoreCase);
            var tasks = new List<Task<WorkerStatus>>();
            foreach (var channel in workerChannels)
            {
                tasks.Add(channel.GetWorkerStatusAsync());
            }

            await Task.WhenAll(tasks);

            for (int i = 0; i < workerChannels.Length; i++)
            {
                var workerChannel = workerChannels[i];
                var workerStatus = tasks[i].Result;
                _logger.LogDebug($"[HostMonitor] Worker status: ID={workerChannel.Id}, Latency={Math.Round(workerStatus.Latency.TotalMilliseconds)}ms");
                workerStatuses.Add(workerChannel.Id, workerStatus);
            }

            return workerStatuses;
        }

        public async Task ShutdownAsync()
        {
            _logger.LogDebug($"Waiting for {nameof(RpcFunctionInvocationDispatcher)} to shutdown");
            Task timeoutTask = Task.Delay(_shutdownTimeout);
            IList<Task> workerChannelTasks = (await GetInitializedWorkerChannelsAsync()).Select(a => a.DrainInvocationsAsync()).ToList();
            Task completedTask = await Task.WhenAny(timeoutTask, Task.WhenAll(workerChannelTasks));

            if (completedTask.Equals(timeoutTask))
            {
                _logger.LogDebug($"Draining invocations from language worker channel timed out. Shutting down '{nameof(RpcFunctionInvocationDispatcher)}'");
            }
            else
            {
                _logger.LogDebug($"Draining invocations from language worker channel completed. Shutting down '{nameof(RpcFunctionInvocationDispatcher)}'");
            }
        }

        public async Task InvokeAsync(ScriptInvocationContext invocationContext)
        {
            // This could throw if no initialized workers are found. Shut down instance and retry.
            IEnumerable<IRpcWorkerChannel> workerChannels = await GetInitializedWorkerChannelsAsync();
            var rpcWorkerChannel = _functionDispatcherLoadBalancer.GetLanguageWorkerChannel(workerChannels);
            if (rpcWorkerChannel.FunctionInputBuffers.TryGetValue(invocationContext.FunctionMetadata.GetFunctionId(), out BufferBlock<ScriptInvocationContext> bufferBlock))
            {
                _logger.LogTrace("Posting invocation id:{InvocationId} on workerId:{workerChannelId}", invocationContext.ExecutionContext.InvocationId, rpcWorkerChannel.Id);
                rpcWorkerChannel.FunctionInputBuffers[invocationContext.FunctionMetadata.GetFunctionId()].Post(invocationContext);
            }
            else
            {
                throw new InvalidOperationException($"Function:{invocationContext.FunctionMetadata.Name} is not loaded by the language worker: {rpcWorkerChannel.Id}");
            }
        }

        internal async Task<IEnumerable<IRpcWorkerChannel>> GetAllWorkerChannelsAsync()
        {
            var webhostChannelDictionary = _webHostLanguageWorkerChannelManager.GetChannels(_workerRuntime);
            List<IRpcWorkerChannel> webhostChannels = null;
            if (webhostChannelDictionary != null)
            {
                webhostChannels = new List<IRpcWorkerChannel>();
                foreach (var pair in webhostChannelDictionary)
                {
                    var workerChannel = await pair.Value.Task;
                    webhostChannels.Add(workerChannel);
                }
            }

            IEnumerable<IRpcWorkerChannel> workerChannels = webhostChannels == null ? _jobHostLanguageWorkerChannelManager.GetChannels() : webhostChannels.Union(_jobHostLanguageWorkerChannelManager.GetChannels());
            return workerChannels;
        }

        internal async Task<IEnumerable<IRpcWorkerChannel>> GetInitializedWorkerChannelsAsync()
        {
            IEnumerable<IRpcWorkerChannel> workerChannels = await GetAllWorkerChannelsAsync();
            IEnumerable<IRpcWorkerChannel> initializedWorkers = workerChannels.Where(ch => ch.IsChannelReadyForInvocations());

            int workerCount = await _maxProcessCount.Value;
            if (initializedWorkers.Count() > workerCount)
            {
                throw new InvalidOperationException($"Number of initialized language workers exceeded:{initializedWorkers.Count()} exceeded maxProcessCount: {workerCount}");
            }

            return initializedWorkers;
        }

        public async void WorkerError(WorkerErrorEvent workerError)
        {
            if (_disposing || _disposed)
            {
                return;
            }

            try
            {
                if (string.Equals(_workerRuntime, workerError.Language))
                {
                    _logger.LogDebug("Handling WorkerErrorEvent for runtime:{runtime}, workerId:{workerId}. Failed with: {exception}", workerError.Language, _workerRuntime, workerError.Exception);
                    AddOrUpdateErrorBucket(workerError);
                    await DisposeAndRestartWorkerChannel(workerError.Language, workerError.WorkerId, workerError.Exception);
                }
                else
                {
                    _logger.LogDebug("Received WorkerErrorEvent for runtime:{runtime}, workerId:{workerId}", workerError.Language, workerError.WorkerId);
                    _logger.LogDebug("WorkerErrorEvent runtime:{runtime} does not match current runtime:{currentRuntime}. Failed with: {exception}", workerError.Language, _workerRuntime, workerError.Exception);
                }
            }
            catch (TaskCanceledException)
            {
                // Specifically in the "we were torn down while trying to restart" case, we want to catch here and ignore
                // If we don't catch the exception from an async void method, we'll end up tearing down the entire runtime instead
                // It's possible we want to catch *all* exceptions and log or ignore here, but taking the minimal change first
                // For example if we capture and log, we're left in a worker-less state with a working Host runtime - is that desired? Will it self recover elsewhere?
            }
        }

        public async void WorkerRestart(WorkerRestartEvent workerRestart)
        {
            if (_disposing || _disposed)
            {
                return;
            }

            try
            {
                _logger.LogDebug("Handling WorkerRestartEvent for runtime:{runtime}, workerId:{workerId}", workerRestart.Language, workerRestart.WorkerId);
                await DisposeAndRestartWorkerChannel(workerRestart.Language, workerRestart.WorkerId);
            }
            catch (TaskCanceledException)
            {
                // Specifically in the "we were torn down while trying to restart" case, we want to catch here and ignore
                // If we don't catch the exception from an async void method, we'll end up tearing down the entire runtime instead
                // It's possible we want to catch *all* exceptions and log or ignore here, but taking the minimal change first
                // For example if we capture and log, we're left in a worker-less state with a working Host runtime - is that desired? Will it self recover elsewhere?
            }
        }

        public async Task StartWorkerChannel()
        {
            await StartWorkerChannel(null);
        }

        private async Task DisposeAndRestartWorkerChannel(string runtime, string workerId, Exception workerException = null)
        {
            if (_disposing || _disposed)
            {
                return;
            }

            _logger.LogDebug("Attempting to dispose webhost or jobhost channel for workerId: '{channelId}', runtime: '{language}'", workerId, runtime);
            bool isWebHostChannelDisposed = await _webHostLanguageWorkerChannelManager.ShutdownChannelIfExistsAsync(runtime, workerId, workerException);
            bool isJobHostChannelDisposed = false;
            if (!isWebHostChannelDisposed)
            {
                isJobHostChannelDisposed = await _jobHostLanguageWorkerChannelManager.ShutdownChannelIfExistsAsync(workerId, workerException);
            }

            if (!isWebHostChannelDisposed && !isJobHostChannelDisposed)
            {
                _logger.LogDebug("Did not find WebHost or JobHost channel to dispose for workerId: '{channelId}', runtime: '{language}'", workerId, runtime);
            }

            if (ShouldRestartWorkerChannel(runtime, isWebHostChannelDisposed, isJobHostChannelDisposed))
            {
                // Set state to "WorkerProcessRestarting" if there are no other workers to handle work
                if ((await GetInitializedWorkerChannelsAsync()).Count() == 0)
                {
                    State = FunctionInvocationDispatcherState.WorkerProcessRestarting;
                    _logger.LogDebug("No initialized worker channels for runtime '{runtime}'. Delaying future invocations", runtime);
                }
                // Restart worker channel
                _logger.LogDebug("Restarting worker channel for runtime: '{runtime}'", runtime);
                await StartWorkerChannel(runtime);
                // State is set back to "Initialized" when worker channel is up again
            }
            else
            {
                _logger.LogDebug("Skipping worker channel restart for errored worker runtime: '{runtime}', current runtime: '{currentRuntime}', isWebHostChannel: '{isWebHostChannel}', isJobHostChannel: '{isJobHostChannel}'",
                    runtime, _workerRuntime, isWebHostChannelDisposed, isJobHostChannelDisposed);
            }
        }

        internal bool ShouldRestartWorkerChannel(string runtime, bool isWebHostChannel, bool isJobHostChannel)
        {
            return string.Equals(_workerRuntime, runtime, StringComparison.InvariantCultureIgnoreCase) && (isWebHostChannel || isJobHostChannel);
        }

        private async Task StartWorkerChannel(string runtime)
        {
            if (_disposing || _disposed)
            {
                return;
            }

            if (string.IsNullOrEmpty(runtime))
            {
                runtime = _workerRuntime;
            }

            if (_languageWorkerErrors.Count < ErrorEventsThreshold)
            {
                try
                {
                    // Issue only one restart at a time.
                    await _startWorkerProcessLock.WaitAsync();
                    // After waiting on the lock (which could take some time), make sure we're not in a disposed state trying to start things up
                    if (_disposing || _disposed)
                    {
                        return;
                    }
                    await InitializeJobhostLanguageWorkerChannelAsync(_languageWorkerErrors.Count);
                }
                finally
                {
                    // Wait before releasing the lock to give time for the process to startup and initialize.
                    try
                    {
                        await Task.Delay(_restartWait, _disposeToken.Token);
                        _startWorkerProcessLock.Release();
                    }
                    catch (TaskCanceledException)
                    {
                    }
                }
            }
            else if (_jobHostLanguageWorkerChannelManager.GetChannels().Count() == 0)
            {
                _logger.LogError("Exceeded language worker restart retry count for runtime:{runtime}. Shutting down and proactively recycling the Functions Host to recover", runtime);
                _applicationLifetime.StopApplication();
            }
        }

        private void AddOrUpdateErrorBucket(WorkerErrorEvent currentErrorEvent)
        {
            if (_languageWorkerErrors.TryPeek(out WorkerErrorEvent top))
            {
                if ((currentErrorEvent.CreatedAt - top.CreatedAt) > _thresholdBetweenRestarts)
                {
                    while (!_languageWorkerErrors.IsEmpty)
                    {
                        _languageWorkerErrors.TryPop(out WorkerErrorEvent popped);
                        _logger.LogDebug($"Popping out errorEvent createdAt: '{popped.CreatedAt}' workerId: '{popped.WorkerId}'");
                    }
                }
            }
            _languageWorkerErrors.Push(currentErrorEvent);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_disposing)
                {
                    _logger.LogDebug("Disposing FunctionDispatcher");
                    _disposeToken.Cancel();
                    // We're explicitly NOT disposing the token here, because if anything our Task.Delay() is waiting on it
                    // Disposing here gives zero time to observe the cancel and is likely to yield an ObjectDisposeException in a race.
                    // Since this is relatively rare, let the GC clean it up.
                    //_disposeToken.Dispose();
                    _startWorkerProcessLock.Dispose();
                    _workerErrorSubscription.Dispose();
                    _workerRestartSubscription.Dispose();
                    _processStartCancellationToken.Cancel();
                    _processStartCancellationToken.Dispose();
                    _jobHostLanguageWorkerChannelManager.ShutdownChannels();
                }

                State = FunctionInvocationDispatcherState.Disposed;
                _disposed = true;
            }
        }

        public void Dispose()
        {
            _disposing = true;
            State = FunctionInvocationDispatcherState.Disposing;
            Dispose(true);
        }

        public async Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId)
        {
            // Dispose and restart errored channel with the particular invocation id
            var channels = await GetInitializedWorkerChannelsAsync();
            foreach (var channel in channels)
            {
                if (channel.IsExecutingInvocation(invocationId))
                {
                    _logger.LogDebug($"Restarting channel with workerId: '{channel.Id}' that is executing invocation: '{invocationId}' and timed out.");
                    await DisposeAndRestartWorkerChannel(_workerRuntime, channel.Id);
                    return true;
                }
            }
            return false;
        }
    }
}