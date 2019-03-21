// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
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
        private IScriptEventManager _eventManager;
        private IEnumerable<WorkerConfig> _workerConfigs;
        private CreateChannel _channelFactory;
        private ILanguageWorkerChannelManager _languageWorkerChannelManager;
        private ConcurrentDictionary<string, LanguageWorkerState> _workerStates = new ConcurrentDictionary<string, LanguageWorkerState>();
        private IDisposable _workerErrorSubscription;
        private IList<IDisposable> _workerStateSubscriptions = new List<IDisposable>();
        private ScriptJobHostOptions _scriptOptions;
        private bool disposedValue = false;
        private IOptions<ManagedDependencyOptions> _managedDependencyOptions;

        public FunctionDispatcher(IOptions<ScriptJobHostOptions> scriptHostOptions,
            IMetricsLogger metricsLogger,
            IScriptEventManager eventManager,
            ILoggerFactory loggerFactory,
            IOptions<LanguageWorkerOptions> languageWorkerOptions,
            ILanguageWorkerChannelManager languageWorkerChannelManager,
            IOptions<ManagedDependencyOptions> managedDependencyOptions)
        {
            _metricsLogger = metricsLogger;
            _scriptOptions = scriptHostOptions.Value;
            _languageWorkerChannelManager = languageWorkerChannelManager;
            _eventManager = eventManager;
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryFunctionDispatcher);

            _workerErrorSubscription = _eventManager.OfType<WorkerErrorEvent>()
               .Subscribe(WorkerError);
            _managedDependencyOptions = managedDependencyOptions;
        }

        public IDictionary<string, LanguageWorkerState> LanguageWorkerChannelStates => _workerStates;

        internal CreateChannel ChannelFactory
        {
            get
            {
                if (_channelFactory == null)
                {
                    _channelFactory = (language, registrations, attemptCount) =>
                    {
                        var languageWorkerChannel = _languageWorkerChannelManager.CreateLanguageWorkerChannel(Guid.NewGuid().ToString(), _scriptOptions.RootScriptPath, language, registrations, _metricsLogger, attemptCount, false, _managedDependencyOptions);
                        languageWorkerChannel.StartWorkerProcess();
                        return languageWorkerChannel;
                    };
                }
                return _channelFactory;
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

        public LanguageWorkerState CreateWorkerStateWithExistingChannel(string language, ILanguageWorkerChannel languageWorkerChannel)
        {
            WorkerConfig config = _workerConfigs.Where(c => c.Language.Equals(language, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            var state = new LanguageWorkerState();
            state.Channel = languageWorkerChannel;
            state.Channel.RegisterFunctions(state.Functions);
            _workerStates[language] = state;
            return state;
        }

        public void Initialize(string workerRuntime, IEnumerable<FunctionMetadata> functions)
        {
            _languageWorkerChannelManager.ShutdownStandbyChannels(functions);

            if (Utility.IsSupportedRuntime(workerRuntime, _workerConfigs))
            {
                ILanguageWorkerChannel initializedChannel = _languageWorkerChannelManager.GetChannel(workerRuntime);
                if (initializedChannel != null)
                {
                    _logger.LogDebug("Found initialized language worker channel for runtime: {workerRuntime}", workerRuntime);
                    CreateWorkerStateWithExistingChannel(workerRuntime, initializedChannel);
                }
                else
                {
                    _logger.LogDebug("Creating new language worker channel for runtime:{workerRuntime}", workerRuntime);
                    CreateWorkerState(workerRuntime);
                }
            }
        }

        private LanguageWorkerState CreateWorkerState(string runtime)
        {
            var state = new LanguageWorkerState();
            WorkerConfig config = _workerConfigs.Where(c => c.Language.Equals(runtime, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            state.Channel = ChannelFactory(runtime, state.Functions, 0);
            _workerStates[runtime] = state;
            return state;
        }

        public void Register(FunctionRegistrationContext context)
        {
            _workerStates[context.Metadata.Language].Functions.OnNext(context);
        }

        public void WorkerError(WorkerErrorEvent workerError)
        {
            if (_workerStates.TryGetValue(workerError.Language, out LanguageWorkerState erroredWorkerState))
            {
                _logger.LogDebug("Handling WorkerErrorEvent for runtime:{runtime}", workerError.Language);
                erroredWorkerState.Errors.Add(workerError.Exception);
                bool isPreInitializedChannel = _languageWorkerChannelManager.ShutdownChannelIfExists(workerError.Language);
                if (!isPreInitializedChannel)
                {
                    _logger.LogDebug("Disposing errored channel for workerId: {channelId}, for runtime:{language}", erroredWorkerState.Channel.Id, workerError.Language);
                    erroredWorkerState.Channel.Dispose();
                }
                _logger.LogDebug("Restarting worker channel for runtime:{runtime}", workerError.Language);
                RestartWorkerChannel(workerError.Language, erroredWorkerState);
            }
        }

        private void RestartWorkerChannel(string runtime, LanguageWorkerState erroredWorkerState)
        {
            if (erroredWorkerState.Errors.Count < 3)
            {
                erroredWorkerState.Channel = CreateNewChannelWithExistingWorkerState(runtime, erroredWorkerState);
                _workerStates[runtime] = erroredWorkerState;
            }
            else
            {
                _logger.LogDebug("Exceeded language worker restart retry count for runtime:{runtime}", runtime);
                PublishWorkerProcessErrorEvent(runtime, erroredWorkerState);
            }
        }

        private void PublishWorkerProcessErrorEvent(string runtime, LanguageWorkerState erroredWorkerState)
        {
            var exMessage = $"Failed to start language worker for: {runtime}";
            var languageWorkerChannelException = (erroredWorkerState.Errors != null && erroredWorkerState.Errors.Count > 0) ? new LanguageWorkerChannelException(exMessage, new AggregateException(erroredWorkerState.Errors.ToList())) : new LanguageWorkerChannelException(exMessage);
            var errorBlock = new ActionBlock<ScriptInvocationContext>(ctx =>
            {
                ctx.ResultSource.TrySetException(languageWorkerChannelException);
            });
            _workerStateSubscriptions.Add(erroredWorkerState.Functions.Subscribe(reg =>
            {
                erroredWorkerState.AddRegistration(reg);
                reg.InputBuffer.LinkTo(errorBlock);
            }));
            _eventManager.Publish(new WorkerProcessErrorEvent(erroredWorkerState.Channel.Id, runtime, languageWorkerChannelException));
        }

        private ILanguageWorkerChannel CreateNewChannelWithExistingWorkerState(string language, LanguageWorkerState erroredWorkerState)
        {
            WorkerConfig config = _workerConfigs.Where(c => c.Language.Equals(language, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            var newWorkerChannel = ChannelFactory(language, erroredWorkerState.Functions, erroredWorkerState.Errors.Count);
            newWorkerChannel.RegisterFunctions(erroredWorkerState.Functions);
            return newWorkerChannel;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _workerErrorSubscription.Dispose();
                    foreach (var subscription in _workerStateSubscriptions)
                    {
                        subscription.Dispose();
                    }
                    foreach (var pair in _workerStates)
                    {
                        // TODO #3296 - send WorkerTerminate message to shut down language worker process gracefully (instead of just a killing)
                        // WebhostLanguageWorkerChannels life time is managed by LanguageWorkerChannelManager
                        if (!pair.Value.Channel.IsWebhostChannel)
                        {
                            pair.Value.Channel.Dispose();
                        }
                        pair.Value.Functions.Dispose();
                    }
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
