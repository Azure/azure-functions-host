// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class FunctionRegistry : IFunctionRegistry
    {
        private IScriptEventManager _eventManager;
        private IRpcServer _server;
        private CreateChannel _channelFactory;
        private TraceWriter _traceWriter;
        private List<WorkerConfig> _workerConfigs;

        private ConcurrentDictionary<WorkerConfig, WorkerState> _channelState = new ConcurrentDictionary<WorkerConfig, WorkerState>();

        private IDisposable _workerErrorSubscription;
        private List<ILanguageWorkerChannel> _erroredChannels = new List<ILanguageWorkerChannel>();
        private bool disposedValue = false;

        public FunctionRegistry(
            IScriptEventManager manager,
            IRpcServer server,
            CreateChannel channelFactory,
            TraceWriter traceWriter,
            IEnumerable<WorkerConfig> workers)
        {
            _eventManager = manager;
            _server = server;
            _channelFactory = channelFactory;
            _traceWriter = traceWriter;
            _workerConfigs = workers?.ToList() ?? new List<WorkerConfig>();

            _workerErrorSubscription = _eventManager.OfType<WorkerErrorEvent>()
                .Subscribe(WorkerError);
        }

        public bool IsSupported(FunctionMetadata functionMetadata)
        {
            return _workerConfigs.Any(config => config.Extension == Path.GetExtension(functionMetadata.ScriptFile));
        }

        internal WorkerState CreateWorkerState(WorkerConfig config)
        {
            var state = new WorkerState();
            state.Channel = _channelFactory(config, state.Functions);
            return state;
        }

        public void Register(FunctionRegistrationContext context)
        {
            WorkerConfig workerConfig = _workerConfigs.First(config => config.Extension == Path.GetExtension(context.Metadata.ScriptFile));
            var state = _channelState.GetOrAdd(workerConfig, CreateWorkerState);
            state.Functions.OnNext(context);
        }

        public void WorkerError(WorkerErrorEvent workerError)
        {
            // TODO: move retry logic, possibly into worker channel decorator
            _channelState.AddOrUpdate(workerError.Worker.Config,
                CreateWorkerState,
                (config, state) =>
                {
                    _erroredChannels.Add(state.Channel);
                    state.Errors.Add(workerError.Exception);
                    if (state.Errors.Count < 3)
                    {
                        state.Channel = _channelFactory(config, state.Functions);
                    }
                    else
                    {
                        var exception = new AggregateException(state.Errors.ToList());
                        var errorBlock = new ActionBlock<ScriptInvocationContext>(ctx =>
                        {
                            ctx.ResultSource.TrySetException(exception);
                        });
                        state.Functions.Subscribe(reg => reg.InputBuffer.LinkTo(errorBlock));
                    }
                    return state;
                });
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _workerErrorSubscription.Dispose();
                    foreach (var pair in _channelState)
                    {
                        pair.Value.Channel.Dispose();
                        pair.Value.Functions.Dispose();
                    }
                    foreach (var channel in _erroredChannels)
                    {
                        channel.Dispose();
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

        internal class WorkerState
        {
            internal ILanguageWorkerChannel Channel { get; set; }

            internal List<Exception> Errors { get; set; } = new List<Exception>();

            // Registered list of functions which can be replayed if the worker fails to start / errors
            internal ReplaySubject<FunctionRegistrationContext> Functions { get; set; } = new ReplaySubject<FunctionRegistrationContext>();
        }
    }
}
