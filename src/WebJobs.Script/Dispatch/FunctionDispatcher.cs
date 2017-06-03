// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
#pragma warning disable SA1200 // Using directives must be placed correctly
    using WorkerPool = ICollection<ILanguageWorkerChannel>;
#pragma warning restore SA1200 // Using directives must be placed correctly

    internal class FunctionDispatcher : IFunctionDispatcher
    {
        private readonly ScriptHostConfiguration _scriptConfig;
        private readonly IScriptEventManager _eventManager;
        private readonly TraceWriter _logger;

        // TODO: support user defined ScriptType definitions
        private readonly IDictionary<ScriptType, WorkerPool> _workerMapping = new Dictionary<ScriptType, WorkerPool>();
        private ICollection<ILanguageWorkerChannel> _workers = new List<ILanguageWorkerChannel>();

        // TODO: handle dead connections https://news.ycombinator.com/item?id=12345223
        private GrpcServer _server;

        public FunctionDispatcher(ScriptHostConfiguration scriptConfig, IScriptEventManager manager, TraceWriter logger)
        {
            _scriptConfig = scriptConfig;
            _eventManager = manager;
            _logger = logger;

            _server = new GrpcServer();

            // TODO need to start grpc service before any of the workers
            _server.Start();

            // TODO Add only if there are java script functions
            AddWorkers(new List<LanguageWorkerConfig>()
            {
                new NodeLanguageWorkerConfig()
            });
        }

        public Task InitializeAsync(IEnumerable<LanguageWorkerConfig> workers)
        {
            // _server.Start();
             AddWorkers(workers);

            // TODO: how to handle async subscriptions? post 'handlefileevent' completed back to event stream?
             _eventManager.OfType<FileEvent>()
                .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal))
                .Subscribe(OnFileEventReceived);

             var workerStartTasks = _workers.Select(worker => worker.StartAsync());
             return Task.WhenAll(workerStartTasks);
        }

        public void OnFileEventReceived(FileEvent fileEvent)
        {
            var workerFileChangeTasks = _workers.Select(worker => worker.HandleFileEventAsync(fileEvent.FileChangeArguments));
            Task.WhenAll(workerFileChangeTasks).GetAwaiter().GetResult();
        }

        public Task<string> LoadAsync(FunctionMetadata functionMetadata)
        {
            return GetWorker(functionMetadata).LoadAsync(functionMetadata);
        }

        public Task<object> InvokeAsync(FunctionMetadata functionMetadata, Dictionary<string, object> scriptExecutionContext)
        {
            return GetWorker(functionMetadata).InvokeAsync(scriptExecutionContext);
        }

        public async Task ShutdownAsync()
        {
            var workerStopTasks = _workers.Select(worker => worker.StopAsync());
            await Task.WhenAll(workerStopTasks);
            await _server.ShutdownAsync();
        }

        private void AddWorkers(IEnumerable<LanguageWorkerConfig> workerConfigs)
        {
            if (workerConfigs != null)
            {
                foreach (var workerConfig in workerConfigs)
                {
                    var worker = new LanguageWorkerChannel(_scriptConfig, workerConfig, _logger, _server.Connections);
                    _workers.Add(worker);

                    foreach (var scriptType in workerConfig.SupportedScriptTypes)
                    {
                        WorkerPool workerPool = _workerMapping.GetOrAdd(scriptType, key => new List<ILanguageWorkerChannel>());
                        workerPool.Add(worker);
                    }
                }
            }
        }

        private ILanguageWorkerChannel GetWorker(FunctionMetadata functionMetadata)
        {
            var pool = _workerMapping[functionMetadata.ScriptType];
            return SchedulingStrategy(pool);
        }

        private ILanguageWorkerChannel SchedulingStrategy(WorkerPool pool)
        {
            return pool.First();
        }
    }
}
