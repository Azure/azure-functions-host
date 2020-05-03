// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public class RpcWorkerChannelFactory : IRpcWorkerChannelFactory
    {
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IRpcWorkerProcessFactory _rpcWorkerProcessFactory = null;
        private readonly IScriptEventManager _eventManager = null;
        private readonly IEnvironment _environment = null;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions = null;

        public RpcWorkerChannelFactory(IScriptEventManager eventManager, IEnvironment environment, IRpcServer rpcServer, ILoggerFactory loggerFactory, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IRpcWorkerProcessFactory rpcWorkerProcessManager)
        {
            _eventManager = eventManager;
            _loggerFactory = loggerFactory;
            _rpcWorkerProcessFactory = rpcWorkerProcessManager;
            _environment = environment;
            _applicationHostOptions = applicationHostOptions;
        }

        public IRpcWorkerChannel Create(string scriptRootPath, string runtime, IMetricsLogger metricsLogger, int attemptCount, IList<RpcWorkerConfig> workerConfigs)
        {
            var languageWorkerConfig = workerConfigs.Where(c => c.Description.Language.Equals(runtime, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
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
                         _environment,
                         _applicationHostOptions);
        }
    }
}
