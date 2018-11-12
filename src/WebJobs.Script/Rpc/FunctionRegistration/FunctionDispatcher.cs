// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class FunctionDispatcher : IFunctionDispatcher
    {
        private IScriptEventManager _eventManager;
        private IRpcServer _server;
        private LanguageWorkerState _javaWorkerState;
        private IEnumerable<WorkerConfig> _workerConfigs;
        private ConcurrentDictionary<WorkerConfig, LanguageWorkerState> _channelStates = new ConcurrentDictionary<WorkerConfig, LanguageWorkerState>();
        private IDisposable _workerErrorSubscription;
        private IList<IDisposable> _workerStateSubscriptions = new List<IDisposable>();
        private ConcurrentDictionary<string, ILanguageWorkerChannel> _channelsDictionary = new ConcurrentDictionary<string, ILanguageWorkerChannel>();
        private string _language;
        private bool disposedValue = false;

        public FunctionDispatcher(
            IScriptEventManager manager,
            IRpcServer server,
            IEnumerable<WorkerConfig> workerConfigs,
            string language)
        {
            _eventManager = manager;
            _server = server;
            _language = language;
            _workerConfigs = workerConfigs ?? throw new ArgumentNullException("workerConfigs");
            _workerErrorSubscription = _eventManager.OfType<WorkerErrorEvent>()
                .Subscribe(WorkerError);
        }

        public IDictionary<WorkerConfig, LanguageWorkerState> LanguageWorkerChannelStates => _channelStates;

        public bool IsSupported(FunctionMetadata functionMetadata)
        {
            if (string.IsNullOrEmpty(functionMetadata.Language))
            {
                return false;
            }
            if (string.IsNullOrEmpty(_language))
            {
                return true;
            }
            return functionMetadata.Language.Equals(_language, StringComparison.OrdinalIgnoreCase);
        }

        public LanguageWorkerState CreateWorkerState(WorkerConfig config, ILanguageWorkerChannel languageWorkerChannel)
        {
            var state = new LanguageWorkerState();
            state.Channel = languageWorkerChannel;
            state.Channel.WorkerReady(state.Functions);
            _channelsDictionary[state.Channel.Id] = state.Channel;
            _javaWorkerState = state;
            return state;
        }

        public void Register(FunctionRegistrationContext context)
        {
            WorkerConfig workerConfig = _workerConfigs.First(config => config.Extensions.Contains(Path.GetExtension(context.Metadata.ScriptFile)));
            var state = _channelStates.GetOrAdd(workerConfig, _javaWorkerState);
            state.Functions.OnNext(context);
        }

        public void WorkerError(WorkerErrorEvent workerError)
        {
            ILanguageWorkerChannel erroredChannel;
            if (_channelsDictionary.TryGetValue(workerError.WorkerId, out erroredChannel))
            {
                // TODO: move retry logic, possibly into worker channel decorator
                _channelStates.AddOrUpdate(erroredChannel.Config,
                    _javaWorkerState,
                    (config, state) =>
                    {
                        erroredChannel.Dispose();
                        state.Errors.Add(workerError.Exception);
                        if (state.Errors.Count < 3)
                        {
                            // TODO: figure out process restarts
                            state.Channel = null;
                            _channelsDictionary[state.Channel.Id] = state.Channel;
                        }
                        else
                        {
                            var exMessage = $"Failed to start language worker for: {config.Language}";
                            var languageWorkerChannelException = (state.Errors != null && state.Errors.Count > 0) ? new LanguageWorkerChannelException(exMessage, new AggregateException(state.Errors.ToList())) : new LanguageWorkerChannelException(exMessage);
                            var errorBlock = new ActionBlock<ScriptInvocationContext>(ctx =>
                            {
                                ctx.ResultSource.TrySetException(languageWorkerChannelException);
                            });
                            _workerStateSubscriptions.Add(state.Functions.Subscribe(reg =>
                            {
                                state.AddRegistration(reg);
                                reg.InputBuffer.LinkTo(errorBlock);
                            }));
                            _eventManager.Publish(new WorkerProcessErrorEvent(state.Channel.Id, config.Language, languageWorkerChannelException));
                        }
                        return state;
                    });
            }
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
                    foreach (var pair in _channelStates)
                    {
                        // TODO #3296 - send WorkerTerminate message to shut down language worker process gracefully (instead of just a killing)
                        pair.Value.Channel.Dispose();
                        pair.Value.Functions.Dispose();
                    }
                    //_server.ShutdownAsync().ContinueWith(t => t.Exception.Handle(e => true), TaskContinuationOptions.OnlyOnFaulted);
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
