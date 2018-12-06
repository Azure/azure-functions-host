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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class FunctionDispatcher : IFunctionDispatcher
    {
        private IScriptEventManager _eventManager;
        private IMetricsLogger _metricsLogger;
        private IEnumerable<WorkerConfig> _workerConfigs;
        private CreateChannel _channelFactory;
        private ILanguageWorkerChannelManager _languageWorkerChannelManager;
        private ConcurrentDictionary<string, LanguageWorkerState> _workerStates = new ConcurrentDictionary<string, LanguageWorkerState>();
        private IDisposable _workerErrorSubscription;
        private IList<IDisposable> _workerStateSubscriptions = new List<IDisposable>();
        private ILoggerFactory _loggerFactory;
        private ScriptJobHostOptions _scriptOptions;
        private bool disposedValue = false;

        public FunctionDispatcher(IOptions<ScriptJobHostOptions> scriptHostOptions,
            IMetricsLogger metricsLogger,
            IScriptEventManager eventManager,
            ILoggerFactory loggerFactory,
            IOptions<LanguageWorkerOptions> languageWorkerOptions,
            ILanguageWorkerChannelManager languageWorkerChannelManager)
        {
            _metricsLogger = metricsLogger;
            _scriptOptions = scriptHostOptions.Value;
            _languageWorkerChannelManager = languageWorkerChannelManager;
            _eventManager = eventManager;
            _loggerFactory = loggerFactory;
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;

            _workerErrorSubscription = _eventManager.OfType<WorkerErrorEvent>()
               .Subscribe(WorkerError);
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
                        var languageWorkerChannel = _languageWorkerChannelManager.CreateLanguageWorkerChannel(Guid.NewGuid().ToString(), _scriptOptions.RootScriptPath, language, registrations, _metricsLogger, attemptCount);
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

        public LanguageWorkerState CreateWorkerState(string language)
        {
            ILanguageWorkerChannel initializedChannel = _languageWorkerChannelManager.GetChannel(language);
            if (initializedChannel != null)
            {
                return CreateWorkerStateWithExistingChannel(language, initializedChannel);
            }
            else
            {
                var state = new LanguageWorkerState();
                WorkerConfig config = _workerConfigs.Where(c => c.Language.Equals(language, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                state.Channel = ChannelFactory(language, state.Functions, 0);
                _workerStates[language] = state;
                return state;
            }
        }

        public void Register(FunctionRegistrationContext context)
        {
            var state = _workerStates.GetOrAdd(context.Metadata.Language, CreateWorkerState);
            state.Functions.OnNext(context);
        }

        public void WorkerError(WorkerErrorEvent workerError)
        {
            LanguageWorkerState erroredWorkerState;
            if (_workerStates.TryGetValue(workerError.Language, out erroredWorkerState))
            {
                erroredWorkerState.Errors.Add(workerError.Exception);
                bool isPreInitializedChannel = _languageWorkerChannelManager.ShutdownChannelIfExists(workerError.Language);
                if (!isPreInitializedChannel)
                {
                    erroredWorkerState.Channel.Dispose();
                }
                RestartWorkerChannel(workerError.Language, erroredWorkerState);
            }
        }

        private void RestartWorkerChannel(string language, LanguageWorkerState erroredWorkerState)
        {
            if (erroredWorkerState.Errors.Count < 3)
            {
                erroredWorkerState.Channel = CreateNewChannelWithExistingWorkerState(language, erroredWorkerState);
                _workerStates[language] = erroredWorkerState;
            }
            else
            {
                PublishWorkerProcessErrorEvent(language, erroredWorkerState);
            }
        }

        private void PublishWorkerProcessErrorEvent(string language, LanguageWorkerState erroredWorkerState)
        {
            var exMessage = $"Failed to start language worker for: {language}";
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
            _eventManager.Publish(new WorkerProcessErrorEvent(erroredWorkerState.Channel.Id, language, languageWorkerChannelException));
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
                        pair.Value.Channel.Dispose();
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
