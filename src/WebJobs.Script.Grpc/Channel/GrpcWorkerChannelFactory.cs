// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public class GrpcWorkerChannelFactory : IRpcWorkerChannelFactory
    {
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IRpcWorkerProcessFactory _rpcWorkerProcessFactory = null;
        private readonly IScriptEventManager _eventManager = null;
        private readonly IEnvironment _environment = null;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions = null;
        private readonly ISharedMemoryManager _sharedMemoryManager = null;
        private readonly IOptions<WorkerConcurrencyOptions> _workerConcurrencyOptions;
        private readonly IOptions<FunctionsHostingConfigOptions> _hostingConfigOptions;
        private readonly IHttpProxyService _httpProxyService;

        public GrpcWorkerChannelFactory(IScriptEventManager eventManager, IEnvironment environment, ILoggerFactory loggerFactory,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IRpcWorkerProcessFactory rpcWorkerProcessManager, ISharedMemoryManager sharedMemoryManager,
            IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions, IOptions<FunctionsHostingConfigOptions> hostingConfigOptions, IHttpProxyService httpProxyService)
        {
            _eventManager = eventManager;
            _loggerFactory = loggerFactory;
            _rpcWorkerProcessFactory = rpcWorkerProcessManager;
            _environment = environment;
            _applicationHostOptions = applicationHostOptions;
            _sharedMemoryManager = sharedMemoryManager;
            _workerConcurrencyOptions = workerConcurrencyOptions;
            _hostingConfigOptions = hostingConfigOptions;
            _httpProxyService = httpProxyService;
        }

        public IRpcWorkerChannel Create(string scriptRootPath, string runtime, IMetricsLogger metricsLogger, int attemptCount, IEnumerable<RpcWorkerConfig> workerConfigs)
        {
            var languageWorkerConfig = workerConfigs.FirstOrDefault(c => c.Description.Language.Equals(runtime, StringComparison.OrdinalIgnoreCase));
            if (languageWorkerConfig == null)
            {
                throw new InvalidOperationException($"WorkerConfig for runtime: {runtime} not found");
            }
            string workerId = Guid.NewGuid().ToString();
            _eventManager.AddGrpcChannels(workerId); // prepare the inbound/outbound dedicated channels
            ILogger workerLogger = _loggerFactory.CreateLogger($"Worker.LanguageWorkerChannel.{runtime}.{workerId}");
            IWorkerProcess rpcWorkerProcess = _rpcWorkerProcessFactory.Create(workerId, runtime, scriptRootPath, languageWorkerConfig);

            return CreateInternal(workerId, _eventManager, languageWorkerConfig, rpcWorkerProcess, workerLogger, metricsLogger, attemptCount,
                _environment, _applicationHostOptions, _sharedMemoryManager, _workerConcurrencyOptions, _hostingConfigOptions, _httpProxyService);
        }

        internal virtual IRpcWorkerChannel CreateInternal(string workerId, IScriptEventManager eventManager, RpcWorkerConfig languageWorkerConfig, IWorkerProcess rpcWorkerProcess,
            ILogger workerLogger, IMetricsLogger metricsLogger, int attemptCount, IEnvironment environment, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
            ISharedMemoryManager sharedMemoryManager, IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions, IOptions<FunctionsHostingConfigOptions> hostingConfigOptions, IHttpProxyService httpProxyService)
        {
            return new GrpcWorkerChannel(
                         workerId,
                         eventManager,
                         languageWorkerConfig,
                         rpcWorkerProcess,
                         workerLogger,
                         metricsLogger,
                         attemptCount,
                         environment,
                         applicationHostOptions,
                         sharedMemoryManager,
                         workerConcurrencyOptions,
                         hostingConfigOptions,
                         httpProxyService);
        }
    }
}
