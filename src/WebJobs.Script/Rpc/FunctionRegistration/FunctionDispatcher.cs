// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
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
        private CreateChannel _channelFactory;
        private List<WorkerConfig> _workerConfigs;
        private ConcurrentDictionary<WorkerConfig, LanguageWorkerState> _channelStates = new ConcurrentDictionary<WorkerConfig, LanguageWorkerState>();
        private IDisposable _workerErrorSubscription;
        private IList<IDisposable> _workerStateSubscriptions = new List<IDisposable>();
        private ConcurrentDictionary<string, ILanguageWorkerChannel> _channelsDictionary = new ConcurrentDictionary<string, ILanguageWorkerChannel>();
        private bool disposedValue = false;

        public FunctionDispatcher(
            IScriptEventManager manager,
            IRpcServer server,
            CreateChannel channelFactory,
            IEnumerable<WorkerConfig> workers)
        {
            _eventManager = manager;
            _server = server;
            _channelFactory = channelFactory;
            _workerConfigs = workers?.ToList() ?? new List<WorkerConfig>();
            _workerErrorSubscription = _eventManager.OfType<WorkerErrorEvent>()
                .Subscribe(WorkerError);
        }

        public IDictionary<WorkerConfig, LanguageWorkerState> LanguageWorkerChannelStates => _channelStates;

        public bool IsSupported(FunctionMetadata functionMetadata)
        {
            return _workerConfigs.Any(config => config.Extensions.Contains(Path.GetExtension(functionMetadata.ScriptFile)));
        }

        internal LanguageWorkerState CreateWorkerState(WorkerConfig config)
        {
            var state = new LanguageWorkerState();
            state.Channel = _channelFactory(config, state.Functions, 0);
            _channelsDictionary[state.Channel.Id] = state.Channel;
            return state;
        }

        public void Register(FunctionRegistrationContext context)
        {
            WorkerConfig workerConfig = _workerConfigs.First(config => config.Extensions.Contains(Path.GetExtension(context.Metadata.ScriptFile)));
            var state = _channelStates.GetOrAdd(workerConfig, CreateWorkerState);
            state.Functions.OnNext(context);
        }

        public void WorkerError(WorkerErrorEvent workerError)
        {
            ILanguageWorkerChannel erroredChannel;
            if (_channelsDictionary.TryGetValue(workerError.WorkerId, out erroredChannel))
            {
                // TODO: move retry logic, possibly into worker channel decorator
                _channelStates.AddOrUpdate(erroredChannel.Config,
                    CreateWorkerState,
                    (config, state) =>
                    {
                        erroredChannel.Dispose();
                        state.Errors.Add(workerError.Exception);
                        if (state.Errors.Count < 3)
                        {
                            state.Channel = _channelFactory(config, state.Functions, state.Errors.Count);
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
                                state.RegisteredFunctions.Add(reg);
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
                    _server.ShutdownAsync().GetAwaiter().GetResult();
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
