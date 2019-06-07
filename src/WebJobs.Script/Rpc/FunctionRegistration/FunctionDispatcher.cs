// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
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
        private readonly IEnvironment _environment;
        private readonly IScriptJobHostEnvironment _scriptJobHostEnvironment;
        private readonly IDisposable _rpcChannelReadySubscriptions;
        private readonly int _debounceSeconds = 10;
        private readonly int _maxAllowedProcessCount = 10;
        private IScriptEventManager _eventManager;
        private IEnumerable<WorkerConfig> _workerConfigs;
        private ILanguageWorkerChannelManager _languageWorkerChannelManager;
        private LanguageWorkerState _workerState = new LanguageWorkerState();
        private IDisposable _workerErrorSubscription;
        private ScriptJobHostOptions _scriptOptions;
        private int _maxProcessCount;
        private IFunctionDispatcherLoadBalancer _functionDispatcherLoadBalancer;
        private bool _disposed = false;
        private bool _disposing = false;
        private IOptions<ManagedDependencyOptions> _managedDependencyOptions;
        private string _workerRuntime;
        private Action _shutdownStandbyWorkerChannels;
        private IEnumerable<FunctionMetadata> _functions;

        public FunctionDispatcher(IOptions<ScriptJobHostOptions> scriptHostOptions,
            IMetricsLogger metricsLogger,
            IEnvironment environment,
            IScriptJobHostEnvironment scriptJobHostEnvironment,
            IScriptEventManager eventManager,
            ILoggerFactory loggerFactory,
            IOptions<LanguageWorkerOptions> languageWorkerOptions,
            ILanguageWorkerChannelManager languageWorkerChannelManager,
            IOptions<ManagedDependencyOptions> managedDependencyOptions,
            IFunctionDispatcherLoadBalancer functionDispatcherLoadBalancer)
        {
            _metricsLogger = metricsLogger;
            _scriptOptions = scriptHostOptions.Value;
            _environment = environment;
            _scriptJobHostEnvironment = scriptJobHostEnvironment;
            _languageWorkerChannelManager = languageWorkerChannelManager;
            _eventManager = eventManager;
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _managedDependencyOptions = managedDependencyOptions;
            _logger = loggerFactory.CreateLogger<FunctionDispatcher>();
            _workerRuntime = _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName);

            var processCount = _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionsWorkerProcessCountSettingName);
            _maxProcessCount = (processCount != null && int.Parse(processCount) > 1) ? int.Parse(processCount) : 1;
            _maxProcessCount = _maxProcessCount > _maxAllowedProcessCount ? _maxAllowedProcessCount : _maxProcessCount;
            _functionDispatcherLoadBalancer = functionDispatcherLoadBalancer;

            State = FunctionDispatcherState.Default;

            _workerErrorSubscription = _eventManager.OfType<WorkerErrorEvent>()
               .Subscribe(WorkerError);

            _rpcChannelReadySubscriptions = _eventManager.OfType<RpcJobHostChannelReadyEvent>()
                .ObserveOn(NewThreadScheduler.Default)
                .Subscribe(AddOrUpdateWorkerChannels);

            _shutdownStandbyWorkerChannels = ShutdownWebhostLanguageWorkerChannels;
            _shutdownStandbyWorkerChannels = _shutdownStandbyWorkerChannels.Debounce(5000);
        }

        public FunctionDispatcherState State { get; private set; }

        public LanguageWorkerState WorkerState => _workerState;

        internal int MaxProcessCount => _maxProcessCount;

        internal ILanguageWorkerChannelManager ChannelManager => _languageWorkerChannelManager;

        internal async void InitializeJobhostLanguageWorkerChannelAsync()
        {
            await InitializeJobhostLanguageWorkerChannelAsync(0);
        }

        internal async Task InitializeJobhostLanguageWorkerChannelAsync(int attemptCount)
        {
            var languageWorkerChannel = _languageWorkerChannelManager.CreateLanguageWorkerChannel(Guid.NewGuid().ToString(), _scriptOptions.RootScriptPath, _workerRuntime, _metricsLogger, attemptCount, false, _managedDependencyOptions);
            languageWorkerChannel.SetupFunctionInvocationBuffers(_functions);
            _workerState.AddChannel(languageWorkerChannel);
            await languageWorkerChannel.StartWorkerProcessAsync();
        }

        internal async void InitializeWebhostLanguageWorkerChannel()
        {
            _logger.LogDebug("Creating new webhost language worker channel for runtime:{workerRuntime}.", _workerRuntime);
            ILanguageWorkerChannel workerChannel = await _languageWorkerChannelManager.InitializeChannelAsync(_workerRuntime);
            workerChannel.SetupFunctionInvocationBuffers(_functions);
            workerChannel.SendFunctionLoadRequests();
        }

        internal void ShutdownWebhostLanguageWorkerChannels()
        {
            _logger.LogDebug("{workerRuntimeConstant}={value}. Will shutdown all the worker channels that started in placeholder mode", LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, _workerRuntime);
            _languageWorkerChannelManager.ShutdownChannels();
        }

        private void StartWorkerProcesses(int startIndex, Action startAction)
        {
            for (var count = startIndex; count < _maxProcessCount; count++)
            {
                startAction = startAction.Debounce(count * _debounceSeconds * 1000);
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
                foreach (var functionMetadata in functions)
                {
                    _workerState.Functions.OnNext(functionMetadata);
                }
                State = FunctionDispatcherState.Initializing;
                IEnumerable<ILanguageWorkerChannel> initializedChannels = _languageWorkerChannelManager.GetChannels(_workerRuntime);
                if (initializedChannels != null)
                {
                    foreach (var initializedChannel in initializedChannels)
                    {
                        _logger.LogDebug("Found initialized language worker channel for runtime: {workerRuntime} workerId:{workerId}", _workerRuntime, initializedChannel.Id);
                        initializedChannel.SetupFunctionInvocationBuffers(_functions);
                        initializedChannel.SendFunctionLoadRequests();
                    }
                    StartWorkerProcesses(initializedChannels.Count(), InitializeWebhostLanguageWorkerChannel);
                    State = FunctionDispatcherState.Initialized;
                }
                else
                {
                    await InitializeJobhostLanguageWorkerChannelAsync(0);
                    StartWorkerProcesses(1, InitializeJobhostLanguageWorkerChannelAsync);
                }
            }
        }

        public void Invoke(ScriptInvocationContext invocationContext)
        {
            try
            {
                IEnumerable<ILanguageWorkerChannel> workerChannels = GetInitializedWorkerChannels();
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

        internal IEnumerable<ILanguageWorkerChannel> GetInitializedWorkerChannels()
        {
            IEnumerable<ILanguageWorkerChannel> webhostChannels = _languageWorkerChannelManager.GetChannels(_workerRuntime);
            IEnumerable<ILanguageWorkerChannel> workerChannels = webhostChannels == null ? _workerState.GetChannels() : webhostChannels.Union(_workerState.GetChannels());
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
                _workerState.Errors.Add(workerError.Exception);
                bool isPreInitializedChannel = _languageWorkerChannelManager.ShutdownChannelIfExists(workerError.Language, workerError.WorkerId);
                if (!isPreInitializedChannel)
                {
                    _logger.LogDebug("Disposing errored channel for workerId: {channelId}, for runtime:{language}", workerError.WorkerId, workerError.Language);
                    var erroredChannel = _workerState.GetChannels().Where(ch => ch.Id == workerError.WorkerId).FirstOrDefault();
                    if (erroredChannel != null)
                    {
                        _workerState.DisposeAndRemoveChannel(erroredChannel);
                    }
                }
                _logger.LogDebug("Restarting worker channel for runtime:{runtime}", workerError.Language);
                await RestartWorkerChannel(workerError.Language, workerError.WorkerId);
            }
        }

        private async Task RestartWorkerChannel(string runtime, string workerId)
        {
            if (_workerState.Errors.Count < 3 * _maxProcessCount)
            {
                await InitializeJobhostLanguageWorkerChannelAsync(_workerState.Errors.Count);
            }
            else if (_workerState.GetChannels().Count() == 0)
            {
                _logger.LogError("Exceeded language worker restart retry count for runtime:{runtime}. Shutting down Functions Host", runtime);
                _scriptJobHostEnvironment.Shutdown();
            }
        }

        private void AddOrUpdateWorkerChannels(RpcJobHostChannelReadyEvent rpcChannelReadyEvent)
        {
            if (!_disposing)
            {
                _logger.LogDebug("Adding jobhost language worker channel for runtime: {language}.", rpcChannelReadyEvent.Language);
                rpcChannelReadyEvent.LanguageWorkerChannel.SendFunctionLoadRequests();
                State = FunctionDispatcherState.Initialized;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _workerErrorSubscription.Dispose();
                _rpcChannelReadySubscriptions.Dispose();
                _workerState.DisposeAndRemoveChannels();
                _workerState.Functions.Dispose();
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
