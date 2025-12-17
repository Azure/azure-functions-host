// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.InstanceManager.Configuration;
using Microsoft.Azure.Functions.WorkerModel.Configuration;
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
        private readonly GrpcServerOptions _rpcServer;
        private readonly IWorkerConsoleLogSource _consoleLogSource;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IOptions<FunctionsHostingConfigOptions> _hostingConfigOptions;
        private readonly IOptions<WorkerProcessOptions> _processOptions;
        private readonly IEnvironment _environment;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _scriptApplicationHostOptions;

        public RpcWorkerProcessFactory(IOptions<GrpcServerOptions> rpcServer,
                                       IScriptEventManager eventManager,
                                       ILoggerFactory loggerFactory,
                                       IWorkerProcessFactory defaultWorkerProcessFactory,
                                       IProcessRegistry processRegistry,
                                       IWorkerConsoleLogSource consoleLogSource,
                                       IMetricsLogger metricsLogger,
                                       IServiceProvider serviceProvider,
                                       IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
                                       IOptions<WorkerProcessOptions> processOptions,
                                       IOptionsMonitor<ScriptApplicationHostOptions> scriptApplicationHostOptions)
        {
            _loggerFactory = loggerFactory;
            _eventManager = eventManager;
            _rpcServer = rpcServer.Value;
            _consoleLogSource = consoleLogSource;
            _workerProcessFactory = defaultWorkerProcessFactory;
            _processRegistry = processRegistry;
            _metricsLogger = metricsLogger;
            _serviceProvider = serviceProvider;
            _hostingConfigOptions = hostingConfigOptions;
            _processOptions = processOptions;
            _scriptApplicationHostOptions = scriptApplicationHostOptions;
        }

        public IWorkerProcess Create(string workerId, string runtime, string scriptRootPath, RpcWorkerConfig workerConfig)
        {
            ILogger workerProcessLogger = _loggerFactory.CreateLogger($"Worker.rpcWorkerProcess.{runtime}.{workerId}");
            return new RpcWorkerProcess(runtime, workerId, scriptRootPath, _rpcServer.ServerUri, workerConfig, _eventManager, _workerProcessFactory, _processRegistry,
                workerProcessLogger, _consoleLogSource, _metricsLogger, _serviceProvider, _hostingConfigOptions, _processOptions, _scriptApplicationHostOptions, _loggerFactory);
        }
    }
}
