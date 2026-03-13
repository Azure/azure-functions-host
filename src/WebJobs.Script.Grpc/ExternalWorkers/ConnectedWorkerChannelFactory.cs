// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers
{
    /// <summary>
    /// Factory for creating <see cref="ConnectedWorkerChannel"/> instances.
    /// </summary>
    internal class ConnectedWorkerChannelFactory
    {
        private readonly IScriptEventManager _eventManager;
        private readonly IScriptHostManager _hostManager;
        private readonly IEnvironment _environment;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly ISharedMemoryManager _sharedMemoryManager;
        private readonly IOptions<WorkerConcurrencyOptions> _workerConcurrencyOptions;
        private readonly IOptions<FunctionsHostingConfigOptions> _hostingConfigOptions;
        private readonly IHttpProxyService _httpProxyService;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IMetricsLogger _metricsLogger;

        public ConnectedWorkerChannelFactory(
            IScriptEventManager eventManager,
            IScriptHostManager hostManager,
            IEnvironment environment,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
            ISharedMemoryManager sharedMemoryManager,
            IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions,
            IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
            IHttpProxyService httpProxyService,
            ILoggerFactory loggerFactory,
            IMetricsLogger metricsLogger)
        {
            _eventManager = eventManager;
            _hostManager = hostManager;
            _environment = environment;
            _applicationHostOptions = applicationHostOptions;
            _sharedMemoryManager = sharedMemoryManager;
            _workerConcurrencyOptions = workerConcurrencyOptions;
            _hostingConfigOptions = hostingConfigOptions;
            _httpProxyService = httpProxyService;
            _loggerFactory = loggerFactory;
            _metricsLogger = metricsLogger;
        }

        /// <summary>
        /// Creates a new <see cref="ConnectedWorkerChannel"/> for an externally-connected worker.
        /// </summary>
        public ConnectedWorkerChannel Create(string workerId, RpcWorkerConfig workerConfig)
        {
            var logger = _loggerFactory.CreateLogger($"Worker.{workerId}");
            return new ConnectedWorkerChannel(
                workerId, _eventManager, _hostManager, workerConfig,
                logger, _metricsLogger, _environment, _applicationHostOptions,
                _sharedMemoryManager, _workerConcurrencyOptions,
                _hostingConfigOptions, _httpProxyService);
        }
    }
}
