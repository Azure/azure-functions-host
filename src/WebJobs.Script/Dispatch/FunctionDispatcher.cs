// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Description.Script;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class FunctionDispatcher : IFunctionDispatcher
    {
        private IScriptEventManager _eventManager;

        private List<WorkerConfig> _workerConfigs = new List<WorkerConfig>();
        private IDictionary<FunctionMetadata, WorkerConfig> _workerMap = new Dictionary<FunctionMetadata, WorkerConfig>();
        private ConcurrentDictionary<WorkerConfig, ILanguageWorkerChannel> _channelMap = new ConcurrentDictionary<WorkerConfig, ILanguageWorkerChannel>();
        private Func<WorkerConfig, ILanguageWorkerChannel> _channelFactory;

        // TODO: handle dead connections https://news.ycombinator.com/item?id=12345223
        private IRpcServer _server;

        private bool disposedValue = false;

        public FunctionDispatcher(IScriptEventManager manager, IRpcServer server, Func<WorkerConfig, ILanguageWorkerChannel> channelFactory, List<WorkerConfig> workers)
        {
            _eventManager = manager;
            _channelFactory = channelFactory;
            _workerConfigs = workers;

            _server = server;
        }

        public bool TryRegister(FunctionMetadata functionMetadata)
        {
            WorkerConfig workerConfig = _workerConfigs.FirstOrDefault(config => config.Extension == Path.GetExtension(functionMetadata.ScriptFile));
            if (workerConfig != null)
            {
                _workerMap.Add(functionMetadata, workerConfig);
                ILanguageWorkerChannel channel = _channelMap.GetOrAdd(workerConfig, _channelFactory);
                channel.Register(functionMetadata);
                return true;
            }
            return false;
        }

        public Task<ScriptInvocationResult> InvokeAsync(FunctionMetadata functionMetadata, ScriptInvocationContext context)
        {
            var worker = _workerMap[functionMetadata];
            var channel = _channelMap[worker];
            return channel.InvokeAsync(functionMetadata, context);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    foreach (var pair in _channelMap)
                    {
                        var channel = pair.Value;
                        channel.Dispose();
                        _server.ShutdownAsync().GetAwaiter().GetResult();
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
