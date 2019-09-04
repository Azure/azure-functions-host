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

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class FunctionDispatcher : IFunctionDispatcher
    {
        private readonly IMetricsLogger _metricsLogger;
        private readonly ILogger _logger;
        private readonly ILanguageWorkerChannelFactory _languageWorkerChannelFactory;
        private readonly IEnvironment _environment;
        private readonly IScriptJobHostEnvironment _scriptJobHostEnvironment;
        private readonly int _debounceSeconds = 10;
        private readonly int _maxAllowedProcessCount = 10;
        private readonly TimeSpan thresholdBetweenRestarts = TimeSpan.FromMinutes(LanguageWorkerConstants.WorkerRestartErrorIntervalThresholdInMinutes);

        private IScriptEventManager _eventManager;
        private IEnumerable<WorkerConfig> _workerConfigs;
        private IWebHostLanguageWorkerChannelManager _webHostLanguageWorkerChannelManager;
        private IJobHostLanguageWorkerChannelManager _jobHostLanguageWorkerChannelManager;
        private IDisposable _workerErrorSubscription;
        private IDisposable _workerRestartSubscription;
        private ScriptJobHostOptions _scriptOptions;
        private int _maxProcessCount;
        private IFunctionDispatcherLoadBalancer _functionDispatcherLoadBalancer;
        private bool _disposed = false;
        private bool _disposing = false;
        private IOptions<ManagedDependencyOptions> _managedDependencyOptions;
        private string _workerRuntime;
        private Action _shutdownStandbyWorkerChannels;
        private IEnumerable<FunctionMetadata> _functions;
        private ConcurrentStack<WorkerErrorEvent> _languageWorkerErrors = new ConcurrentStack<WorkerErrorEvent>();
        private CancellationTokenSource _processStartCancellationToken = new CancellationTokenSource();

        public FunctionDispatcher(IOptions<ScriptJobHostOptions> scriptHostOptions,
            IMetricsLogger metricsLogger,
            IEnvironment environment,
            IScriptJobHostEnvironment scriptJobHostEnvironment,
            IScriptEventManager eventManager,
            ILoggerFactory loggerFactory,
            ILanguageWorkerChannelFactory languageWorkerChannelFactory,
            IOptions<LanguageWorkerOptions> languageWorkerOptions,
            IWebHostLanguageWorkerChannelManager webHostLanguageWorkerChannelManager,
            IJobHostLanguageWorkerChannelManager jobHostLanguageWorkerChannelManager,
            IOptions<ManagedDependencyOptions> managedDependencyOptions,
            IFunctionDispatcherLoadBalancer functionDispatcherLoadBalancer)
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
            _logger = loggerFactory.CreateLogger<FunctionDispatcher>();
            _languageWorkerChannelFactory = languageWorkerChannelFactory;
            _workerRuntime = _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName);

            var processCount = _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionsWorkerProcessCountSettingName);
            _maxProcessCount = (processCount != null && int.Parse(processCount) > 1) ? int.Parse(processCount) : 1;
            _maxProcessCount = _maxProcessCount > _maxAllowedProcessCount ? _maxAllowedProcessCount : _maxProcessCount;
            _functionDispatcherLoadBalancer = functionDispatcherLoadBalancer;

            State = FunctionDispatcherState.Default;

            _workerErrorSubscription = _eventManager.OfType<WorkerErrorEvent>()
               .Subscribe(WorkerError);
            _workerRestartSubscription = _eventManager.OfType<WorkerRestartEvent>()
               .Subscribe(WorkerRestart);

            _shutdownStandbyWorkerChannels = ShutdownWebhostLanguageWorkerChannels;
            _shutdownStandbyWorkerChannels = _shutdownStandbyWorkerChannels.Debounce(milliseconds: 5000);
        }

        public FunctionDispatcherState State { get; private set; }

        public IJobHostLanguageWorkerChannelManager JobHostLanguageWorkerChannelManager => _jobHostLanguageWorkerChannelManager;

        internal ConcurrentStack<WorkerErrorEvent> LanguageWorkerErrors => _languageWorkerErrors;

        internal int MaxProcessCount => _maxProcessCount;

        internal IWebHostLanguageWorkerChannelManager WebHostLanguageWorkerChannelManager => _webHostLanguageWorkerChannelManager;

        internal async void InitializeJobhostLanguageWorkerChannelAsync()
        {
            await InitializeJobhostLanguageWorkerChannelAsync(0);
        }

        internal Task InitializeJobhostLanguageWorkerChannelAsync(int attemptCount)
        {
            var languageWorkerChannel = _languageWorkerChannelFactory.CreateLanguageWorkerChannel(_scriptOptions.RootScriptPath, _workerRuntime, _metricsLogger, attemptCount, _managedDependencyOptions);
            languageWorkerChannel.SetupFunctionInvocationBuffers(_functions);
            _jobHostLanguageWorkerChannelManager.AddChannel(languageWorkerChannel);
            languageWorkerChannel.StartWorkerProcessAsync().ContinueWith(workerInitTask =>
                 {
                     if (workerInitTask.IsCompleted)
                     {
                         _logger.LogDebug("Adding jobhost language worker channel for runtime: {language}. workerId:{id}", _workerRuntime, languageWorkerChannel.Id);
                         languageWorkerChannel.SendFunctionLoadRequests();
                         State = FunctionDispatcherState.Initialized;
                     }
                     else
                     {
                         _logger.LogWarning("Failed to start language worker process for runtime: {language}. workerId:{id}", _workerRuntime, languageWorkerChannel.Id);
                     }
                 });
            return Task.CompletedTask;
        }

        internal async void InitializeWebhostLanguageWorkerChannel()
        {
            _logger.LogDebug("Creating new webhost language worker channel for runtime:{workerRuntime}.", _workerRuntime);
            ILanguageWorkerChannel workerChannel = await _webHostLanguageWorkerChannelManager.InitializeChannelAsync(_workerRuntime);
            workerChannel.SetupFunctionInvocationBuffers(_functions);
            workerChannel.SendFunctionLoadRequests();
        }

        internal async void ShutdownWebhostLanguageWorkerChannels()
        {
            _logger.LogDebug("{workerRuntimeConstant}={value}. Will shutdown all the worker channels that started in placeholder mode", LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, _workerRuntime);
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

        public bool IsSupported(FunctionMetadata functionMetadata, string workerRuntime)
        {
            if (string.IsNullOrEmpty(functionMetadata.Language))
            {
                return false;
            }
            if (string.IsNullOrEmpty(workerRuntime))
            {
                return true;
            }
            return functionMetadata.Language.Equals(workerRuntime, StringComparison.OrdinalIgnoreCase);
        }

        public async Task InitializeAsync(IEnumerable<FunctionMetadata> functions)
        {
            if (_environment.IsPlaceholderModeEnabled())
            {
                return;
            }

            _workerRuntime = _workerRuntime ?? Utility.GetWorkerRuntime(functions);
            _functions = functions;
            if (string.IsNullOrEmpty(_workerRuntime) || _workerRuntime.Equals(LanguageWorkerConstants.DotNetLanguageWorkerName, StringComparison.InvariantCultureIgnoreCase))
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
                State = FunctionDispatcherState.Initializing;
                Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> webhostLanguageWorkerChannels = _webHostLanguageWorkerChannelManager.GetChannels(_workerRuntime);
                if (webhostLanguageWorkerChannels != null)
                {
                    foreach (string workerId in webhostLanguageWorkerChannels.Keys.ToList())
                    {
                        if (webhostLanguageWorkerChannels.TryGetValue(workerId, out TaskCompletionSource<ILanguageWorkerChannel> initializedLanguageWorkerChannelTask))
                        {
                            _logger.LogDebug("Found initialized language worker channel for runtime: {workerRuntime} workerId:{workerId}", _workerRuntime, workerId);
                            try
                            {
                                ILanguageWorkerChannel initializedLanguageWorkerChannel = await initializedLanguageWorkerChannelTask.Task;
                                initializedLanguageWorkerChannel.SetupFunctionInvocationBuffers(_functions);
                                initializedLanguageWorkerChannel.SendFunctionLoadRequests();
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
                    State = FunctionDispatcherState.Initialized;
                }
                else
                {
                    await InitializeJobhostLanguageWorkerChannelAsync(0);
                    StartWorkerProcesses(1, InitializeJobhostLanguageWorkerChannelAsync);
                }
            }
        }

        public async Task InvokeAsync(ScriptInvocationContext invocationContext)
        {
            try
            {
                IEnumerable<ILanguageWorkerChannel> workerChannels = await GetInitializedWorkerChannelsAsync();
                var languageWorkerChannel = _functionDispatcherLoadBalancer.GetLanguageWorkerChannel(workerChannels, _maxProcessCount);
                if (languageWorkerChannel.FunctionInputBuffers.TryGetValue(invocationContext.FunctionMetadata.FunctionId, out BufferBlock<ScriptInvocationContext> bufferBlock))
                {
                    _logger.LogDebug("Posting invocation id:{InvocationId} on workerId:{workerChannelId}", invocationContext.ExecutionContext.InvocationId, languageWorkerChannel.Id);
                    languageWorkerChannel.FunctionInputBuffers[invocationContext.FunctionMetadata.FunctionId].Post(invocationContext);
                }
                else
                {
                    throw new InvalidOperationException($"Function:{invocationContext.FunctionMetadata.Name} is not loaded by the language worker: {languageWorkerChannel.Id}");
                }
            }
            catch (Exception invokeEx)
            {
                invocationContext.ResultSource.TrySetException(invokeEx);
            }
        }

        internal async Task<IEnumerable<ILanguageWorkerChannel>> GetInitializedWorkerChannelsAsync()
        {
            Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> webhostChannelDictionary = _webHostLanguageWorkerChannelManager.GetChannels(_workerRuntime);
            List<ILanguageWorkerChannel> webhostChannels = null;
            if (webhostChannelDictionary != null)
            {
                webhostChannels = new List<ILanguageWorkerChannel>();
                foreach (string workerId in webhostChannelDictionary.Keys)
                {
                    if (webhostChannelDictionary.TryGetValue(workerId, out TaskCompletionSource<ILanguageWorkerChannel> initializedLanguageWorkerChannelTask))
                    {
                        webhostChannels.Add(await initializedLanguageWorkerChannelTask.Task);
                    }
                }
            }
            IEnumerable<ILanguageWorkerChannel> workerChannels = webhostChannels == null ? _jobHostLanguageWorkerChannelManager.GetChannels() : webhostChannels.Union(_jobHostLanguageWorkerChannelManager.GetChannels());
            IEnumerable<ILanguageWorkerChannel> initializedWorkers = workerChannels.Where(ch => ch.State == LanguageWorkerChannelState.Initialized);
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
                _logger.LogDebug("Handling WorkerErrorEvent for runtime:{runtime}, workerId:{workerId}", workerError.Language, workerError.WorkerId);
                AddOrUpdateErrorBucket(workerError);
                await DisposeAndRestartWorkerChannel(workerError.Language, workerError.WorkerId);
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
                await RestartWorkerChannel(runtime, workerId);
            }
        }

        internal bool ShouldRestartWorkerChannel(string runtime, bool isWebHostChannel, bool isJobHostChannel)
        {
            return _workerRuntime.Equals(runtime, StringComparison.InvariantCultureIgnoreCase) && (isWebHostChannel || isJobHostChannel);
        }

        private async Task RestartWorkerChannel(string runtime, string workerId)
        {
            if (_languageWorkerErrors.Count < 3 * _maxProcessCount)
            {
                await InitializeJobhostLanguageWorkerChannelAsync(_languageWorkerErrors.Count);
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
