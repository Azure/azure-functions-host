// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Description.Script;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;

using WorkerPool = System.Collections.Generic.ICollection
    <Microsoft.Azure.WebJobs.Script.Dispatch.ILanguageWorkerChannel>;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class FunctionDispatcher : IFunctionDispatcher
    {
        private readonly ScriptHostConfiguration _scriptConfig;
        private readonly IScriptEventManager _eventManager;
        private readonly TraceWriter _logger;

        private readonly List<LanguageWorkerConfig> _workerConfigs = new List<LanguageWorkerConfig>();
        private readonly IDictionary<ScriptType, WorkerPool> _workerMapping = new Dictionary<ScriptType, WorkerPool>();
        private ICollection<ILanguageWorkerChannel> _workers = new List<ILanguageWorkerChannel>();

        // TODO: handle dead connections https://news.ycombinator.com/item?id=12345223
        private GrpcServer _server;
        private FunctionRpcImpl _serverImpl;

        public FunctionDispatcher(ScriptHostConfiguration scriptConfig, IScriptEventManager manager, TraceWriter logger, List<LanguageWorkerConfig> defaultWorkers)
        {
            _scriptConfig = scriptConfig;
            _eventManager = manager;
            _logger = logger;

            _serverImpl = new FunctionRpcImpl(_eventManager);
            _server = new GrpcServer(_serverImpl);

            _server.Start();

            AddWorkers(defaultWorkers);
        }

        public bool TryRegister(FunctionMetadata functionMetadata)
        {
            var worker = GetWorker(functionMetadata);
            if (worker != null)
            {
                worker.StartAsync();
                return true;
            }
            return false;
        }

        public Task<ScriptInvocationResult> InvokeAsync(FunctionMetadata functionMetadata, ScriptInvocationContext context)
        {
            return GetWorker(functionMetadata).InvokeAsync(functionMetadata, context);
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
                    _workerConfigs.Add(workerConfig);
                    var worker = new LanguageWorkerChannel(_scriptConfig, _eventManager, workerConfig, _logger, _server.BoundPort);
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
            return pool.FirstOrDefault();
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
