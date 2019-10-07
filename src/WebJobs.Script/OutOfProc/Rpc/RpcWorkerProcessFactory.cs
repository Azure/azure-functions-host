// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class RpcWorkerProcessFactory : IRpcWorkerProcessFactory
    {
        private readonly IWorkerProcessFactory _workerProcessFactory;
        private readonly IEnumerable<WorkerConfig> _workerConfigs = null;
        private readonly IProcessRegistry _processRegistry;
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IScriptEventManager _eventManager = null;
        private readonly IRpcServer _rpcServer = null;
        private readonly ILanguageWorkerConsoleLogSource _consoleLogSource;

        public RpcWorkerProcessFactory(IRpcServer rpcServer,
                                       IOptions<LanguageWorkerOptions> languageWorkerOptions,
                                       IScriptEventManager eventManager,
                                       ILoggerFactory loggerFactory,
                                       IWorkerProcessFactory defaultWorkerProcessFactory,
                                       IProcessRegistry processRegistry,
                                       ILanguageWorkerConsoleLogSource consoleLogSource)
        {
            _loggerFactory = loggerFactory;
            _eventManager = eventManager;
            _rpcServer = rpcServer;
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _consoleLogSource = consoleLogSource;
            _workerProcessFactory = defaultWorkerProcessFactory;
            _processRegistry = processRegistry;
        }

        public ILanguageWorkerProcess Create(string workerId, string runtime, string scriptRootPath)
        {
            WorkerConfig workerConfig = _workerConfigs.Where(c => c.Description.Language.Equals(runtime, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            ILogger workerProcessLogger = _loggerFactory.CreateLogger($"Worker.LanguageWorkerProcess.{runtime}.{workerId}");
            return new RpcWorkerProcess(runtime, workerId, scriptRootPath, _rpcServer.Uri, workerConfig.Arguments, _eventManager, _workerProcessFactory, _processRegistry, workerProcessLogger, _consoleLogSource);
        }
    }
}
