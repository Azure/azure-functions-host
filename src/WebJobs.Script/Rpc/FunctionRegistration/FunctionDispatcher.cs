// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
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
        private IScriptEventManager _eventManager;
        private IEnumerable<WorkerConfig> _workerConfigs;
        private IWebHostLanguageWorkerChannelManager _webHostLanguageWorkerChannelManager;
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
        private ConcurrentBag<Exception> _languageWorkerErrors = new ConcurrentBag<Exception>();
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
            IOptions<ManagedDependencyOptions> managedDependencyOptions,
            IFunctionDispatcherLoadBalancer functionDispatcherLoadBalancer)
        {
            _metricsLogger = metricsLogger;
            _scriptOptions = scriptHostOptions.Value;
            _environment = environment;
            _scriptJobHostEnvironment = scriptJobHostEnvironment;
            _webHostLanguageWorkerChannelManager = webHostLanguageWorkerChannelManager;
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

            _shutdownStandbyWorkerChannels = ShutdownWebhostLanguageWorkerChannels;
            _shutdownStandbyWorkerChannels = _shutdownStandbyWorkerChannels.Debounce(milliseconds: 5000);
        }

        public FunctionDispatcherState State { get; private set; }

        internal ConcurrentBag<Exception> LanguageWorkerErrors => _languageWorkerErrors;

        internal int MaxProcessCount => _maxProcessCount;

        internal IWebHostLanguageWorkerChannelManager WebHostLanguageWorkerChannelManager => _webHostLanguageWorkerChannelManager;

        internal async Task InitializeLanguageWorkerChannel(int attemptCount)
        {
            _logger.LogDebug("Creating new webhost language worker channel for runtime:{workerRuntime}.", _workerRuntime);
            ILanguageWorkerChannel workerChannel = await _webHostLanguageWorkerChannelManager.InitializeChannelAsync(_workerRuntime);
            var managedDependenciesEnabled = _managedDependencyOptions?.Value != null && _managedDependencyOptions.Value.Enabled;
            workerChannel.SetupFunctionInvocationBuffers(_functions, managedDependenciesEnabled);
            workerChannel.SendFunctionLoadRequests();
        }

        internal void ShutdownWebhostLanguageWorkerChannels()
        {
            _logger.LogDebug("{workerRuntimeConstant}={value}. Will shutdown all the worker channels that started in placeholder mode", LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, _workerRuntime);
            _webHostLanguageWorkerChannelManager.ShutdownChannels();
        }

        private async Task StartWorkerProcesses(int startIndex)
        {
            // Start initializing first right away
            if (startIndex == 0)
            {
                await InitializeLanguageWorkerChannel(0);
            }

            // Offset start of others by _debounceSeconds
            for (var count = startIndex; count < _maxProcessCount; count++)
            {
                Action startAction = async () => await InitializeLanguageWorkerChannel(0);
                startAction = startAction.Debounce(_processStartCancellationToken.Token, count * _debounceSeconds * 1000);
                startAction();
            }

            State = FunctionDispatcherState.Initialized;
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
                var initializedChannelCount = 0;
                State = FunctionDispatcherState.Initializing;

                IEnumerable<ILanguageWorkerChannel> initializedChannels = _webHostLanguageWorkerChannelManager.GetChannels(_workerRuntime);
                if (initializedChannels != null)
                {
                    initializedChannelCount = initializedChannels.Count();
                    foreach (var initializedChannel in initializedChannels)
                    {
                        var managedDependenciesEnabled = _managedDependencyOptions?.Value != null && _managedDependencyOptions.Value.Enabled;
                        _logger.LogDebug("Found initialized language worker channel for runtime: {workerRuntime} workerId:{workerId}", _workerRuntime, initializedChannel.Id);
                        initializedChannel.SetupFunctionInvocationBuffers(_functions, managedDependenciesEnabled);
                        initializedChannel.SendFunctionLoadRequests();
                    }
                }

                await StartWorkerProcesses(initializedChannelCount);
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
            IEnumerable<ILanguageWorkerChannel> workerChannels = _webHostLanguageWorkerChannelManager.GetChannels(_workerRuntime);
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
                _languageWorkerErrors.Add(workerError.Exception);
                bool isPreInitializedChannel = _webHostLanguageWorkerChannelManager.ShutdownChannelIfExists(workerError.Language, workerError.WorkerId);

                // Todo: maybe we don't need this
                if (!isPreInitializedChannel)
                {
                    _logger.LogWarning("Could not find errored language worker in WebHostLanguageWorkerChannelManager. WorkerId: {channelId}. Runtime:{language}.", workerError.WorkerId, workerError.Language);
                }

                _logger.LogDebug("Restarting worker channel for runtime: {runtime}", workerError.Language);
                await RestartWorkerChannel(workerError.Language, workerError.WorkerId);
            }
        }

        private async Task RestartWorkerChannel(string runtime, string workerId)
        {
            // StartWorkerProcesses(initializedChannels.Count(), InitializeWebhostLanguageWorkerChannel);

            if (_languageWorkerErrors.Count < 3 * _maxProcessCount)
            {
                await InitializeLanguageWorkerChannel(_languageWorkerErrors.Count);
            }
            else if (_webHostLanguageWorkerChannelManager.GetChannels(runtime).Count() == 0)
            {
                _logger.LogError("Exceeded language worker restart retry count for runtime:{runtime}. Shutting down Functions Host", runtime);
                _scriptJobHostEnvironment.Shutdown();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _workerErrorSubscription.Dispose();
                _processStartCancellationToken.Cancel();
                _processStartCancellationToken.Dispose();
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
