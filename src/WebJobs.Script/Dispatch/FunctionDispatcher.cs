// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.RPC.Grpc;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
#pragma warning disable SA1200 // Using directives must be placed correctly
    using WorkerPool = ICollection<ILanguageWorkerChannel>;
#pragma warning restore SA1200 // Using directives must be placed correctly

    internal class FunctionDispatcher : IFunctionDispatcher
    {
        private readonly ScriptHostConfiguration _scriptConfig;

        // TODO: support user defined ScriptType definitions
        private readonly IDictionary<ScriptType, WorkerPool> _workerMapping = new Dictionary<ScriptType, WorkerPool>();
        private ICollection<ILanguageWorkerChannel> _workers = new List<ILanguageWorkerChannel>();

        // TODO: handle dead connections https://news.ycombinator.com/item?id=12345223
        private GrpcServer _server;

        public FunctionDispatcher(ScriptHostConfiguration scriptConfig)
        {
            _scriptConfig = scriptConfig;
            _server = new GrpcServer();
            AddWorkers(new List<LanguageWorkerConfig>()
            {
                new NodeLanguageWorkerConfig()
            });
        }

        public Task InitializeAsync(IEnumerable<LanguageWorkerConfig> workers)
        {
            AddWorkers(workers);
            _server.Start();
            var workerStartTasks = _workers.Select(worker => worker.StartAsync());
            return Task.WhenAll(workerStartTasks);
        }

        public Task HandleFileEventAsync(FileSystemEventArgs fileEvent)
        {
            var workerFileChangeTasks = _workers.Select(worker => worker.HandleFileEventAsync(fileEvent));
            return Task.WhenAll();
        }

        public Task LoadAsync(FunctionMetadata functionMetadata)
        {
            return GetWorker(functionMetadata).LoadAsync(functionMetadata);
        }

        public Task<object> InvokeAsync(FunctionMetadata functionMetadata, object[] parameters)
        {
            return GetWorker(functionMetadata).InvokeAsync(parameters);
        }

        public async Task ShutdownAsync()
        {
            var workerStopTasks = _workers.Select(worker => worker.StopAsync());
            await Task.WhenAll(workerStopTasks);
            await _server.ShutdownAsync();
        }

        private void AddWorkers(IEnumerable<LanguageWorkerConfig> configs)
        {
            if (configs != null)
            {
                foreach (var config in configs)
                {
                    var worker = new LanguageWorkerChannel(config);
                    _workers.Add(worker);

                    foreach (var scriptType in config.SupportedScriptTypes)
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
