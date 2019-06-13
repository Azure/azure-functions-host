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
    public class LanguageWorkerProcessFactory : ILanguageWorkerProcessFactory
    {
        private readonly IWorkerProcessFactory _processFactory;
        private readonly IEnumerable<WorkerConfig> _workerConfigs = null;
        private readonly IProcessRegistry _processRegistry;
        private readonly ILogger _logger = null;
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IScriptEventManager _eventManager = null;
        private readonly IRpcServer _rpcServer = null;
        private readonly ILanguageWorkerConsoleLogSource _consoleLogSource;

        public LanguageWorkerProcessFactory(IRpcServer rpcServer,
                                       IOptions<LanguageWorkerOptions> languageWorkerOptions,
                                       IScriptEventManager eventManager,
                                       ILoggerFactory loggerFactory,
                                       ILanguageWorkerConsoleLogSource consoleLogSource)
        {
            _loggerFactory = loggerFactory;
            _eventManager = eventManager;
            _rpcServer = rpcServer;
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _consoleLogSource = consoleLogSource;

            _processFactory = new DefaultWorkerProcessFactory();
            try
            {
                _processRegistry = ProcessRegistryFactory.Create();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Unable to create process registry");
            }
        }

        public ILanguageWorkerProcess CreateLanguageWorkerProcess(string workerId, string runtime, string scriptRootPath)
        {
            WorkerConfig workerConfig = _workerConfigs.Where(c => c.Language.Equals(runtime, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            return new LanguageWorkerProcess(runtime, workerId, scriptRootPath, _rpcServer.Uri, workerConfig.Arguments, _eventManager, _processFactory, _processRegistry, _loggerFactory, _consoleLogSource);
        }
    }
}
