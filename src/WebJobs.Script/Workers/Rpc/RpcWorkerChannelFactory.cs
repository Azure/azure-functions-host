// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public class RpcWorkerChannelFactory : IRpcWorkerChannelFactory
    {
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IRpcWorkerProcessFactory _rpcWorkerProcessFactory = null;
        private readonly IScriptEventManager _eventManager = null;
        private readonly IEnumerable<RpcWorkerConfig> _workerConfigs = null;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions = null;

        public RpcWorkerChannelFactory(IScriptEventManager eventManager, IEnvironment environment, IRpcServer rpcServer, ILoggerFactory loggerFactory, IOptions<LanguageWorkerOptions> languageWorkerOptions,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IRpcWorkerProcessFactory rpcWorkerProcessManager)
        {
            _eventManager = eventManager;
            _loggerFactory = loggerFactory;
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _rpcWorkerProcessFactory = rpcWorkerProcessManager;
            _applicationHostOptions = applicationHostOptions;
        }

        public IRpcWorkerChannel Create(string scriptRootPath, string runtime, IMetricsLogger metricsLogger, int attemptCount)
        {
            var languageWorkerConfig = _workerConfigs.Where(c => c.Description.Language.Equals(runtime, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (languageWorkerConfig == null)
            {
                throw new InvalidOperationException($"WorkerCofig for runtime: {runtime} not found");
            }
            string workerId = Guid.NewGuid().ToString();
            ILogger workerLogger = _loggerFactory.CreateLogger($"Worker.LanguageWorkerChannel.{runtime}.{workerId}");
            IWorkerProcess rpcWorkerProcess = _rpcWorkerProcessFactory.Create(workerId, runtime, scriptRootPath);
            return new RpcWorkerChannel(
                         workerId,
                         _eventManager,
                         languageWorkerConfig,
                         rpcWorkerProcess,
                         workerLogger,
                         metricsLogger,
                         attemptCount,
                         _applicationHostOptions);
        }
    }
}
