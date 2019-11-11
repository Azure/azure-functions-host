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
        private readonly IScriptJobHostEnvironment _scriptJobHostEnvironment;
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
            IScriptJobHostEnvironment scriptJobHostEnvironment,
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
            _scriptJobHostEnvironment = scriptJobHostEnvironment;
            _webHostLanguageWorkerChannelManager = webHostLanguageWorkerChannelManager;
            _jobHostLanguageWorkerChannelManager = jobHostLanguageWorkerChannelManager;
            _eventManager = eventManager;
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _managedDependencyOptions = managedDependencyOptions;
            _logger = loggerFactory.CreateLogger<RpcFunctionInvocationDispatcher>();
            _rpcWorkerChannelFactory = rpcWorkerChannelFactory;
            _workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);

            var processCount = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName);
            _maxProcessCount = (processCount != null && int.Parse(processCount) > 1) ? int.Parse(processCount) : 1;
            _maxProcessCount = _maxProcessCount > _maxAllowedProcessCount ? _maxAllowedProcessCount : _maxProcessCount;
            _functionDispatcherLoadBalancer = functionDispatcherLoadBalancer;

            State = FunctionInvocationDispatcherState.Default;

            _workerErrorSubscription = _eventManager.OfType<WorkerErrorEvent>()
               .Subscribe(WorkerError);
            _workerRestartSubscription = _eventManager.OfType<WorkerRestartEvent>()
               .Subscribe(WorkerRestart);

            _shutdownStandbyWorkerChannels = ShutdownWebhostLanguageWorkerChannels;
            _shutdownStandbyWorkerChannels = _shutdownStandbyWorkerChannels.Debounce(milliseconds: 5000);
        }

        public FunctionInvocationDispatcherState State { get; private set; }

        public IJobHostRpcWorkerChannelManager JobHostLanguageWorkerChannelManager => _jobHostLanguageWorkerChannelManager;

        internal ConcurrentStack<WorkerErrorEvent> LanguageWorkerErrors => _languageWorkerErrors;

        internal int MaxProcessCount => _maxProcessCount;

        internal IWebHostRpcWorkerChannelManager WebHostLanguageWorkerChannelManager => _webHostLanguageWorkerChannelManager;

        internal void InitializeJobhostLanguageWorkerChannel()
        {
            InitializeJobhostLanguageWorkerChannel(0);
        }

        internal void InitializeJobhostLanguageWorkerChannel(int attemptCount)
        {
            var rpcWorkerChannel = _rpcWorkerChannelFactory.Create(_scriptOptions.RootScriptPath, _workerRuntime, _metricsLogger, attemptCount, _managedDependencyOptions);
            _logger.LogDebug("Adding jobhost language worker channel for runtime: {language}. workerId:{id}", _workerRuntime, rpcWorkerChannel.Id);
            _jobHostLanguageWorkerChannelManager.AddChannel(rpcWorkerChannel);
            rpcWorkerChannel.StartWorkerProcess();
            rpcWorkerChannel.SendFunctionLoadRequests(_functions);
        }

        internal void InitializeWebhostLanguageWorkerChannel()
        {
            _logger.LogDebug("Creating new webhost language worker channel for runtime:{workerRuntime}.", _workerRuntime);
            IRpcWorkerChannel workerChannel = _webHostLanguageWorkerChannelManager.InitializeChannel(_workerRuntime);
            workerChannel.SendFunctionLoadRequests(_functions);
        }

        internal void ShutdownWebhostLanguageWorkerChannels()
        {
            _logger.LogDebug("{workerRuntimeConstant}={value}. Will shutdown all the worker channels that started in placeholder mode", RpcWorkerConstants.FunctionWorkerRuntimeSettingName, _workerRuntime);
            _webHostLanguageWorkerChannelManager?.ShutdownChannels();
        }

        private void StartWorkerProcesses(int startIndex, Action startAction)
        {
            for (var count = startIndex; count < _maxProcessCount; count++)
            {
                startAction = startAction.Debounce(_processStartCancellationToken.Token, count * _debounceSeconds * 1000);
                startAction();
            }
        }

        public Task InitializeAsync(IEnumerable<FunctionMetadata> functions)
        {
            if (_environment.IsPlaceholderModeEnabled())
            {
                return Task.CompletedTask;
            }

            _workerRuntime = _workerRuntime ?? Utility.GetWorkerRuntime(functions);
            _functions = functions;
            if (string.IsNullOrEmpty(_workerRuntime) || _workerRuntime.Equals(RpcWorkerConstants.DotNetLanguageWorkerName, StringComparison.InvariantCultureIgnoreCase))
            {
                // Shutdown any placeholder channels for empty function apps or dotnet function apps.
                // This is needed as specilization does not kill standby placeholder channels if worker runtime is not set.
                // Debouce to ensure this does not effect cold start
                _shutdownStandbyWorkerChannels();
                return Task.CompletedTask;
            }

            if (functions == null || functions.Count() == 0)
            {
                // do not initialize function dispachter if there are no functions
                return Task.CompletedTask;
            }

            if (Utility.IsSupportedRuntime(_workerRuntime, _workerConfigs))
            {
                State = FunctionInvocationDispatcherState.Initializing;
                Dictionary<string, IRpcWorkerChannel> webhostLanguageWorkerChannels = _webHostLanguageWorkerChannelManager.GetChannels(_workerRuntime);
                if (webhostLanguageWorkerChannels != null)
                {
                    foreach (string workerId in webhostLanguageWorkerChannels.Keys.ToList())
                    {
                        if (webhostLanguageWorkerChannels.TryGetValue(workerId, out IRpcWorkerChannel initializedLanguageWorkerChannel))
                        {
                            _logger.LogDebug("Found initialized language worker channel for runtime: {workerRuntime} workerId:{workerId}", _workerRuntime, workerId);
                            try
                            {
                                initializedLanguageWorkerChannel.SendFunctionLoadRequests(_functions);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Removing errored webhost language worker channel for runtime: {workerRuntime} workerId:{workerId}", _workerRuntime, workerId);
                                _webHostLanguageWorkerChannelManager.ShutdownChannelIfExists(_workerRuntime, workerId);
                                InitializeWebhostLanguageWorkerChannel();
                            }
                        }
                    }
                    StartWorkerProcesses(webhostLanguageWorkerChannels.Count(), InitializeWebhostLanguageWorkerChannel);
                    State = FunctionInvocationDispatcherState.Initialized;
                }
                else
                {
                    InitializeJobhostLanguageWorkerChannel(0);
                    State = FunctionInvocationDispatcherState.Initialized;
                    StartWorkerProcesses(1, InitializeJobhostLanguageWorkerChannel);
                }
            }
            return Task.CompletedTask;
        }

        public async Task ShutdownAsync()
        {
            _logger.LogDebug($"Waiting for {nameof(RpcFunctionInvocationDispatcher)} to shutdown");
            Task timeoutTask = Task.Delay(_shutdownTimeout);
            IList<Task> workerChannelTasks = GetInitializedWorkerChannelsAsync().Select(a => a.DrainInvocationsAsync()).ToList();
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
            try
            {
                IEnumerable<IRpcWorkerChannel> workerChannels = GetInitializedWorkerChannelsAsync();
                var rpcWorkerChannel = _functionDispatcherLoadBalancer.GetLanguageWorkerChannel(workerChannels, _maxProcessCount);
                _logger.LogDebug("Posting invocation id:{InvocationId} on workerId:{workerChannelId}", invocationContext.ExecutionContext.InvocationId, rpcWorkerChannel.Id);
                await rpcWorkerChannel.SendInvocationRequest(invocationContext);
            }
            catch (Exception invokeEx)
            {
                invocationContext.ResultSource.TrySetException(invokeEx);
            }
        }

        internal IEnumerable<IRpcWorkerChannel> GetInitializedWorkerChannelsAsync()
        {
            Dictionary<string, IRpcWorkerChannel> webhostChannelDictionary = _webHostLanguageWorkerChannelManager.GetChannels(_workerRuntime);
            List<IRpcWorkerChannel> webhostChannels = null;
            if (webhostChannelDictionary != null)
            {
                webhostChannels = new List<IRpcWorkerChannel>();
                foreach (string workerId in webhostChannelDictionary.Keys)
                {
                    if (webhostChannelDictionary.TryGetValue(workerId, out IRpcWorkerChannel initializedLanguageWorkerChannel))
                    {
                        webhostChannels.Add(initializedLanguageWorkerChannel);
                    }
                }
            }
            IEnumerable<IRpcWorkerChannel> workerChannels = webhostChannels == null ? _jobHostLanguageWorkerChannelManager.GetChannels() : webhostChannels.Union(_jobHostLanguageWorkerChannelManager.GetChannels());
            IEnumerable<IRpcWorkerChannel> initializedWorkers = workerChannels.Where(ch => ch.State == RpcWorkerChannelState.Initialized);
            if (initializedWorkers.Count() > _maxProcessCount)
            {
                throw new InvalidOperationException($"Number of initialized language workers exceeded:{initializedWorkers.Count()} exceeded maxProcessCount: {_maxProcessCount}");
            }
            return initializedWorkers;
        }

        public void WorkerError(WorkerErrorEvent workerError)
        {
            if (!_disposing)
            {
                _logger.LogDebug("Handling WorkerErrorEvent for runtime:{runtime}, workerId:{workerId}", workerError.Language, workerError.WorkerId);
                AddOrUpdateErrorBucket(workerError);
                DisposeAndRestartWorkerChannel(workerError.Language, workerError.WorkerId);
            }
        }

        public void WorkerRestart(WorkerRestartEvent workerRestart)
        {
            if (!_disposing)
            {
                _logger.LogDebug("Handling WorkerRestartEvent for runtime:{runtime}, workerId:{workerId}", workerRestart.Language, workerRestart.WorkerId);
                DisposeAndRestartWorkerChannel(workerRestart.Language, workerRestart.WorkerId);
            }
        }

        private void DisposeAndRestartWorkerChannel(string runtime, string workerId)
        {
            bool isWebHostChannel = _webHostLanguageWorkerChannelManager.ShutdownChannelIfExists(runtime, workerId);
            bool isJobHostChannel = false;
            if (!isWebHostChannel)
            {
                _logger.LogDebug("Disposing channel for workerId: {channelId}, for runtime:{language}", workerId, runtime);
                var channel = _jobHostLanguageWorkerChannelManager.GetChannels().Where(ch => ch.Id == workerId).FirstOrDefault();
                if (channel != null)
                {
                    isJobHostChannel = true;
                    _jobHostLanguageWorkerChannelManager.DisposeAndRemoveChannel(channel);
                }
            }
            if (ShouldRestartWorkerChannel(runtime, isWebHostChannel, isJobHostChannel))
            {
                _logger.LogDebug("Restarting worker channel for runtime:{runtime}", runtime);
                RestartWorkerChannel(runtime, workerId);
            }
        }

        internal bool ShouldRestartWorkerChannel(string runtime, bool isWebHostChannel, bool isJobHostChannel)
        {
            return string.Equals(_workerRuntime, runtime, StringComparison.InvariantCultureIgnoreCase) && (isWebHostChannel || isJobHostChannel);
        }

        private void RestartWorkerChannel(string runtime, string workerId)
        {
            if (_languageWorkerErrors.Count < 3 * _maxProcessCount)
            {
                InitializeJobhostLanguageWorkerChannel(_languageWorkerErrors.Count);
            }
            else if (_jobHostLanguageWorkerChannelManager.GetChannels().Count() == 0)
            {
                _logger.LogError("Exceeded language worker restart retry count for runtime:{runtime}. Shutting down Functions Host", runtime);
                _scriptJobHostEnvironment.Shutdown();
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
                _disposed = true;
            }
        }

        public void Dispose()
        {
            _disposing = true;
            Dispose(true);
        }
    }
}
