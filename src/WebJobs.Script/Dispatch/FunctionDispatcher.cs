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

using WorkerPool = System.Collections.Generic.ICollection
    <Microsoft.Azure.WebJobs.Script.Dispatch.ILanguageWorkerChannel>;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
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

            _server.Start();

            // TODO Add only if there are java script functions
            AddWorkers(new List<LanguageWorkerConfig>()
            {
                new NodeLanguageWorkerConfig()
            });
        }

        public Task InitializeAsync(IEnumerable<LanguageWorkerConfig> workers)
        {
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

        public void Load(FunctionMetadata functionMetadata)
        {
            GetWorker(functionMetadata).LoadAsync(functionMetadata);
        }

        public Task<object> InvokeAsync(FunctionMetadata functionMetadata, Dictionary<string, object> scriptExecutionContext)
        {
            return GetWorker(functionMetadata).InvokeAsync(functionMetadata, scriptExecutionContext);
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
                    var worker = new LanguageWorkerChannel(_scriptConfig, workerConfig, _logger, _server);
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

        private static ILanguageWorkerChannel SchedulingStrategy(WorkerPool pool)
        {
            return pool.First();
        }

        public void Dispose()
        {
            foreach (var worker in _workers)
            {
                worker.Dispose();
            }
        }
    }
}
