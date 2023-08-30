// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    internal class RpcWorkerProcessFactory : IRpcWorkerProcessFactory
    {
        private readonly IWorkerProcessFactory _workerProcessFactory;
        private readonly IProcessRegistry _processRegistry;
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IScriptEventManager _eventManager = null;
        private readonly IRpcServer _rpcServer = null;
        private readonly IWorkerConsoleLogSource _consoleLogSource;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IOptions<FunctionsHostingConfigOptions> _hostingConfigOptions;
        private readonly IEnvironment _environment;

        public RpcWorkerProcessFactory(IRpcServer rpcServer,
                                       IScriptEventManager eventManager,
                                       ILoggerFactory loggerFactory,
                                       IWorkerProcessFactory defaultWorkerProcessFactory,
                                       IProcessRegistry processRegistry,
                                       IWorkerConsoleLogSource consoleLogSource,
                                       IMetricsLogger metricsLogger,
                                       IServiceProvider serviceProvider,
                                       IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
                                       IEnvironment environment)
        {
            _loggerFactory = loggerFactory;
            _eventManager = eventManager;
            _rpcServer = rpcServer;
            _consoleLogSource = consoleLogSource;
            _workerProcessFactory = defaultWorkerProcessFactory;
            _processRegistry = processRegistry;
            _metricsLogger = metricsLogger;
            _serviceProvider = serviceProvider;
            _hostingConfigOptions = hostingConfigOptions;
            _environment = environment;
        }

        public IWorkerProcess Create(string workerId, string runtime, string scriptRootPath, RpcWorkerConfig workerConfig)
        {
            ILogger workerProcessLogger = _loggerFactory.CreateLogger($"Worker.rpcWorkerProcess.{runtime}.{workerId}");
            return new RpcWorkerProcess(runtime, workerId, scriptRootPath, _rpcServer.Uri, workerConfig, _eventManager, _workerProcessFactory, _processRegistry, workerProcessLogger, _consoleLogSource, _metricsLogger, _serviceProvider, _hostingConfigOptions, _environment, _loggerFactory);
        }
    }
}
