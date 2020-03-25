// Copyright (c) .NET Foundation. All rights reserved.
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
        private readonly int _debounceSeconds = 10;
        private readonly int _maxAllowedProcessCount = 10;
        private readonly TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(10);
        private readonly TimeSpan thresholdBetweenRestarts = TimeSpan.FromMinutes(WorkerConstants.WorkerRestartErrorIntervalThresholdInMinutes);

        private IScriptEventManager _eventManager;
        private IEnumerable<RpcWorkerConfig> _workerConfigs;
        private IWebHostRpcWorkerChannelManager _webHostLanguageWorkerChannelManager;
        private IJobHostRpcWorkerChannelManager _jobHostLanguageWorkerChannelManager;
        private IDisposable _workerErrorSubscription;
        private IDisposable _workerRestartSubscription;
        private ScriptJobHostOptions _scriptOptions;
        private int _maxProcessCount;
        private IRpcFunctionInvocationDispatcherLoadBalancer _functionDispatcherLoadBalancer;
        private bool _disposed = false;
        private bool _disposing = false;
        private IOptions<ManagedDependencyOptions> _managedDependencyOptions;
        private string _workerRuntime;
        private Action _shutdownStandbyWorkerChannels;
        private IEnumerable<FunctionMetadata> _functions;
        private ConcurrentStack<WorkerErrorEvent> _languageWorkerErrors = new ConcurrentStack<WorkerErrorEvent>();
        private CancellationTokenSource _processStartCancellationToken = new CancellationTokenSource();

        public RpcFunctionInvocationDispatcher(IOptions<ScriptJobHostOptions> scriptHostOptions,
            IMetricsLogger metricsLogger,
            IEnvironment environment,
            IApplicationLifetime applicationLifetime,
            IScriptEventManager eventManager,
            ILoggerFactory loggerFactory,
            IRpcWorkerChannelFactory rpcWorkerChannelFactory,
            IOptions<LanguageWorkerOptions> languageWorkerOptions,
            IWebHostRpcWorkerChannelManager webHostLanguageWorkerChannelManager,
            IJobHostRpcWorkerChannelManager jobHostLanguageWorkerChannelManager,
            IOptions<ManagedDependencyOptions> managedDependencyOptions,
            IRpcFunctionInvocationDispatcherLoadBalancer functionDispatcherLoadBalancer)
        {
            _metricsLogger = metricsLogger;
            _scriptOptions = scriptHostOptions.Value;
            _environment = environment;
            _applicationLifetime = applicationLifetime;
            _webHostLanguageWorkerChannelManager = webHostLanguageWorkerChannelManager;
            _jobHostLanguageWorkerChannelManager = jobHostLanguageWorkerChannelManager;
            _eventManager = eventManager;
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _managedDependencyOptions = managedDependencyOptions ?? throw new ArgumentNullException(nameof(managedDependencyOptions));
            _logger = loggerFactory.CreateLogger<RpcFunctionInvocationDispatcher>();
            _rpcWorkerChannelFactory = rpcWorkerChannelFactory;
            _workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);

            var processCount = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName);
            _maxProcessCount = (processCount != null && int.Parse(processCount) > 1) ? int.Parse(processCount) : 1;
            _maxProcessCount = _maxProcessCount > _maxAllowedProcessCount ? _maxAllowedProcessCount : _maxProcessCount;
            _functionDispatcherLoadBalancer = functionDispatcherLoadBalancer;

            State = FunctionInvocationDispatcherState.Default;
            ErrorEventsThreshold = 3 * _maxProcessCount;

            _workerErrorSubscription = _eventManager.OfType<WorkerErrorEvent>()
               .Subscribe(WorkerError);
            _workerRestartSubscription = _eventManager.OfType<WorkerRestartEvent>()
               .Subscribe(WorkerRestart);

            _shutdownStandbyWorkerChannels = ShutdownWebhostLanguageWorkerChannels;
            _shutdownStandbyWorkerChannels = _shutdownStandbyWorkerChannels.Debounce(milliseconds: 5000);
        }

        public FunctionInvocationDispatcherState State { get; private set; }

        public int ErrorEventsThreshold { get; private set; }

        public IJobHostRpcWorkerChannelManager JobHostLanguageWorkerChannelManager => _jobHostLanguageWorkerChannelManager;

        internal ConcurrentStack<WorkerErrorEvent> LanguageWorkerErrors => _languageWorkerErrors;

        internal int MaxProcessCount => _maxProcessCount;

        internal IWebHostRpcWorkerChannelManager WebHostLanguageWorkerChannelManager => _webHostLanguageWorkerChannelManager;

        internal async void InitializeJobhostLanguageWorkerChannelAsync()
        {
            await InitializeJobhostLanguageWorkerChannelAsync(0);
        }

        internal Task InitializeJobhostLanguageWorkerChannelAsync(int attemptCount)
        {
            var rpcWorkerChannel = _rpcWorkerChannelFactory.Create(_scriptOptions.RootScriptPath, _workerRuntime, _metricsLogger, attemptCount);
            rpcWorkerChannel.SetupFunctionInvocationBuffers(_functions);
            _jobHostLanguageWorkerChannelManager.AddChannel(rpcWorkerChannel);
            rpcWorkerChannel.StartWorkerProcessAsync().ContinueWith(workerInitTask =>
            {
                if (workerInitTask.IsCompleted)
                {
                    _logger.LogDebug("Adding jobhost language worker channel for runtime: {language}. workerId:{id}", _workerRuntime, rpcWorkerChannel.Id);
                    rpcWorkerChannel.SendFunctionLoadRequests(_managedDependencyOptions.Value);
                    State = FunctionInvocationDispatcherState.Initialized;
                }
                else
                {
                    _logger.LogWarning("Failed to start language worker process for runtime: {language}. workerId:{id}", _workerRuntime, rpcWorkerChannel.Id);
                }
            });
            return Task.CompletedTask;
        }

        internal async void InitializeWebhostLanguageWorkerChannel()
        {
            _logger.LogDebug("Creating new webhost language worker channel for runtime:{workerRuntime}.", _workerRuntime);
            IRpcWorkerChannel workerChannel = await _webHostLanguageWorkerChannelManager.InitializeChannelAsync(_workerRuntime);
            workerChannel.SetupFunctionInvocationBuffers(_functions);
            workerChannel.SendFunctionLoadRequests(_managedDependencyOptions.Value);
        }

        internal async void ShutdownWebhostLanguageWorkerChannels()
        {
            _logger.LogDebug("{workerRuntimeConstant}={value}. Will shutdown all the worker channels that started in placeholder mode", RpcWorkerConstants.FunctionWorkerRuntimeSettingName, _workerRuntime);
            await _webHostLanguageWorkerChannelManager?.ShutdownChannelsAsync();
        }

        private void StartWorkerProcesses(int startIndex, Action startAction)
        {
            for (var count = startIndex; count < _maxProcessCount; count++)
            {
                startAction = startAction.Debounce(_processStartCancellationToken.Token, count * _debounceSeconds * 1000);
                startAction();
            }
        }

        public async Task InitializeAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken = default)
        {
            if (_environment.IsPlaceholderModeEnabled())
            {
                return;
            }

            _workerRuntime = _workerRuntime ?? Utility.GetWorkerRuntime(functions);
            _functions = functions;
            if (string.IsNullOrEmpty(_workerRuntime) || _workerRuntime.Equals(RpcWorkerConstants.DotNetLanguageWorkerName, StringComparison.InvariantCultureIgnoreCase))
            {
                // Shutdown any placeholder channels for empty function apps or dotnet function apps.
                // This is needed as specilization does not kill standby placeholder channels if worker runtime is not set.
                // Debouce to ensure this does not effect cold start
                _shutdownStandbyWorkerChannels();
                return;
            }

            if (functions == null || functions.Count() == 0)
            {
                // do not initialize function dispachter if there are no functions
                return;
            }

            if (Utility.IsSupportedRuntime(_workerRuntime, _workerConfigs))
            {
                State = FunctionInvocationDispatcherState.Initializing;
                Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> webhostLanguageWorkerChannels = _webHostLanguageWorkerChannelManager.GetChannels(_workerRuntime);
                if (webhostLanguageWorkerChannels != null)
                {
                    foreach (string workerId in webhostLanguageWorkerChannels.Keys.ToList())
                    {
                        if (webhostLanguageWorkerChannels.TryGetValue(workerId, out TaskCompletionSource<IRpcWorkerChannel> initializedLanguageWorkerChannelTask))
                        {
                            _logger.LogDebug("Found initialized language worker channel for runtime: {workerRuntime} workerId:{workerId}", _workerRuntime, workerId);
                            try
                            {
                                IRpcWorkerChannel initializedLanguageWorkerChannel = await initializedLanguageWorkerChannelTask.Task;
                                initializedLanguageWorkerChannel.SetupFunctionInvocationBuffers(_functions);
                                initializedLanguageWorkerChannel.SendFunctionLoadRequests(_managedDependencyOptions.Value);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Removing errored webhost language worker channel for runtime: {workerRuntime} workerId:{workerId}", _workerRuntime, workerId);
                                await _webHostLanguageWorkerChannelManager.ShutdownChannelIfExistsAsync(_workerRuntime, workerId);
                                InitializeWebhostLanguageWorkerChannel();
                            }
                        }
                    }
                    StartWorkerProcesses(webhostLanguageWorkerChannels.Count(), InitializeWebhostLanguageWorkerChannel);
                    State = FunctionInvocationDispatcherState.Initialized;
                }
                else
                {
                    await InitializeJobhostLanguageWorkerChannelAsync(0);
                    StartWorkerProcesses(1, InitializeJobhostLanguageWorkerChannelAsync);
                }
            }
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
            var rpcWorkerChannel = _functionDispatcherLoadBalancer.GetLanguageWorkerChannel(workerChannels, _maxProcessCount);
            if (rpcWorkerChannel.FunctionInputBuffers.TryGetValue(invocationContext.FunctionMetadata.FunctionId, out BufferBlock<ScriptInvocationContext> bufferBlock))
            {
                _logger.LogDebug("Posting invocation id:{InvocationId} on workerId:{workerChannelId}", invocationContext.ExecutionContext.InvocationId, rpcWorkerChannel.Id);
                rpcWorkerChannel.FunctionInputBuffers[invocationContext.FunctionMetadata.FunctionId].Post(invocationContext);
            }
            else
            {
                throw new InvalidOperationException($"Function:{invocationContext.FunctionMetadata.Name} is not loaded by the language worker: {rpcWorkerChannel.Id}");
            }
        }

        internal async Task<IEnumerable<IRpcWorkerChannel>> GetAllWorkerChannelsAsync()
        {
            Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> webhostChannelDictionary = _webHostLanguageWorkerChannelManager.GetChannels(_workerRuntime);
            List<IRpcWorkerChannel> webhostChannels = null;
            if (webhostChannelDictionary != null)
            {
                webhostChannels = new List<IRpcWorkerChannel>();
                foreach (string workerId in webhostChannelDictionary.Keys)
                {
                    if (webhostChannelDictionary.TryGetValue(workerId, out TaskCompletionSource<IRpcWorkerChannel> initializedLanguageWorkerChannelTask))
                    {
                        webhostChannels.Add(await initializedLanguageWorkerChannelTask.Task);
                    }
                }
            }
            IEnumerable<IRpcWorkerChannel> workerChannels = webhostChannels == null ? _jobHostLanguageWorkerChannelManager.GetChannels() : webhostChannels.Union(_jobHostLanguageWorkerChannelManager.GetChannels());
            return workerChannels;
        }

        internal async Task<IEnumerable<IRpcWorkerChannel>> GetInitializedWorkerChannelsAsync()
        {
            IEnumerable<IRpcWorkerChannel> workerChannels = await GetAllWorkerChannelsAsync();
            IEnumerable<IRpcWorkerChannel> initializedWorkers = workerChannels.Where(ch => ch.IsChannelReadyForInvocations());
            if (initializedWorkers.Count() > _maxProcessCount)
            {
                throw new InvalidOperationException($"Number of initialized language workers exceeded:{initializedWorkers.Count()} exceeded maxProcessCount: {_maxProcessCount}");
            }

            return initializedWorkers;
        }

        public async void WorkerError(WorkerErrorEvent workerError)
        {
            if (!_disposing)
            {
                if (string.Equals(_workerRuntime, workerError.Language))
                {
                    _logger.LogDebug("Handling WorkerErrorEvent for runtime:{runtime}, workerId:{workerId}. Failed with: {exception}", workerError.Language, _workerRuntime, workerError.Exception);
                    AddOrUpdateErrorBucket(workerError);
                    await DisposeAndRestartWorkerChannel(workerError.Language, workerError.WorkerId);
                }
                else
                {
                    _logger.LogDebug("Received WorkerErrorEvent for runtime:{runtime}, workerId:{workerId}", workerError.Language, workerError.WorkerId);
                    _logger.LogDebug("WorkerErrorEvent runtime:{runtime} does not match current runtime:{currentRuntime}. Failed with: {exception}", workerError.Language, _workerRuntime, workerError.Exception);
                }
            }
        }

        public async void WorkerRestart(WorkerRestartEvent workerRestart)
        {
            if (!_disposing)
            {
                _logger.LogDebug("Handling WorkerRestartEvent for runtime:{runtime}, workerId:{workerId}", workerRestart.Language, workerRestart.WorkerId);
                await DisposeAndRestartWorkerChannel(workerRestart.Language, workerRestart.WorkerId);
            }
        }

        private async Task DisposeAndRestartWorkerChannel(string runtime, string workerId)
        {
            bool isWebHostChannel = await _webHostLanguageWorkerChannelManager.ShutdownChannelIfExistsAsync(runtime, workerId);
            bool isJobHostChannel = false;

            // Dispose old worker
            if (isWebHostChannel)
            {
                _logger.LogDebug("Disposing WebHost channel for workerId: {channelId}, for runtime:{language}", workerId, runtime);
            }
            else
            {
                var channel = _jobHostLanguageWorkerChannelManager.GetChannels().Where(ch => ch.Id == workerId).FirstOrDefault();
                if (channel != null)
                {
                    _logger.LogDebug("Disposing JobHost channel for workerId: {channelId}, for runtime:{language}", workerId, runtime);
                    isJobHostChannel = true;
                    _jobHostLanguageWorkerChannelManager.DisposeAndRemoveChannel(channel);
                }
                else
                {
                    _logger.LogDebug("Did not find WebHost or JobHost channel to dispose for workerId: {channelId}, runtime:{language}", workerId, runtime);
                }
            }

            if (ShouldRestartWorkerChannel(runtime, isWebHostChannel, isJobHostChannel))
            {
                // Set state to "WorkerProcessRestarting" if there are no other workers to handle work
                if ((await GetInitializedWorkerChannelsAsync()).Count() == 0)
                {
                    State = FunctionInvocationDispatcherState.WorkerProcessRestarting;
                    _logger.LogDebug("No initialized worker channels for runtime '{runtime}'. Delaying future invocations", runtime);
                }
                // Restart worker channel
                _logger.LogDebug("Restarting worker channel for runtime:{runtime}", runtime);
                await RestartWorkerChannel(runtime, workerId);
                // State is set back to "Initialized" when worker channel is up again
            }
            else
            {
                _logger.LogDebug("Skipping worker channel restart for errored worker runtime:{runtime}, current runtime:{currentRuntime}, isWebHostChannel:{isWebHostChannel}, isJobHostChannel:{isJobHostChannel}",
                    runtime, _workerRuntime, isWebHostChannel, isJobHostChannel);
            }
        }

        internal bool ShouldRestartWorkerChannel(string runtime, bool isWebHostChannel, bool isJobHostChannel)
        {
            return string.Equals(_workerRuntime, runtime, StringComparison.InvariantCultureIgnoreCase) && (isWebHostChannel || isJobHostChannel);
        }

        private async Task RestartWorkerChannel(string runtime, string workerId)
        {
            if (_languageWorkerErrors.Count < ErrorEventsThreshold)
            {
                await InitializeJobhostLanguageWorkerChannelAsync(_languageWorkerErrors.Count);
            }
            else if (_jobHostLanguageWorkerChannelManager.GetChannels().Count() == 0)
            {
                _logger.LogError("Exceeded language worker restart retry count for runtime:{runtime}. Shutting down Functions Host", runtime);
                _applicationLifetime.StopApplication();
            }
        }

        private void AddOrUpdateErrorBucket(WorkerErrorEvent currentErrorEvent)
        {
            if (_languageWorkerErrors.TryPeek(out WorkerErrorEvent top))
            {
                if ((currentErrorEvent.CreatedAt - top.CreatedAt) > thresholdBetweenRestarts)
                {
                    while (!_languageWorkerErrors.IsEmpty)
                    {
                        _languageWorkerErrors.TryPop(out WorkerErrorEvent popped);
                        _logger.LogDebug($"Popping out errorEvent createdAt:{popped.CreatedAt} workerId:{popped.WorkerId}");
                    }
                }
            }
            _languageWorkerErrors.Push(currentErrorEvent);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _logger.LogDebug("Disposing FunctionDispatcher");
                _workerErrorSubscription.Dispose();
                _workerRestartSubscription.Dispose();
                _processStartCancellationToken.Cancel();
                _processStartCancellationToken.Dispose();
                _jobHostLanguageWorkerChannelManager.DisposeAndRemoveChannels();
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
            State = FunctionInvocationDispatcherState.WorkerProcessRestarting;
            var channels = await GetInitializedWorkerChannelsAsync();
            foreach (var channel in channels)
            {
                if (channel.IsExecutingInvocation(invocationId))
                {
                    _logger.LogInformation($"Restarting channel '{channel.Id}' that is executing invocation '{invocationId}' and timed out.");
                    await DisposeAndRestartWorkerChannel(_workerRuntime, channel.Id);
                    return true;
                }
            }
            return false;
        }

        public async Task RestartAllWorkersAsync()
        {
            State = FunctionInvocationDispatcherState.WorkerProcessRestarting;
            var channels = await GetAllWorkerChannelsAsync();
            foreach (var channel in channels)
            {
                _logger.LogInformation($"Restarting channel '{channel.Id}' that is as part of restarting all channels.");
                await DisposeAndRestartWorkerChannel(_workerRuntime, channel.Id);
            }
        }
    }
}