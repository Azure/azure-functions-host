// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class LanguageWorkerChannelFactory : ILanguageWorkerChannelFactory
    {
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly ILanguageWorkerProcessFactory _languageWorkerProcessManager = null;
        private readonly IScriptEventManager _eventManager = null;
        private readonly IEnumerable<WorkerConfig> _workerConfigs = null;

        public LanguageWorkerChannelFactory(IScriptEventManager eventManager, IEnvironment environment, IRpcServer rpcServer, ILoggerFactory loggerFactory, IOptions<LanguageWorkerOptions> languageWorkerOptions,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, ILanguageWorkerProcessFactory languageWorkerProcessManager)
        {
            _eventManager = eventManager;
            _loggerFactory = loggerFactory;
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _languageWorkerProcessManager = languageWorkerProcessManager;
        }

        public ILanguageWorkerChannel CreateLanguageWorkerChannel(string scriptRootPath, string runtime, IMetricsLogger metricsLogger, int attemptCount, IOptions<ManagedDependencyOptions> managedDependencyOptions = null)
        {
            var languageWorkerConfig = _workerConfigs.Where(c => c.Language.Equals(runtime, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (languageWorkerConfig == null)
            {
                throw new InvalidOperationException($"WorkerCofig for runtime: {runtime} not found");
            }
            string workerId = Guid.NewGuid().ToString();
            ILogger workerLogger = _loggerFactory.CreateLogger($"Worker.LanguageWorkerChannel.{runtime}.{workerId}");
            ILanguageWorkerProcess languageWorkerProcess = _languageWorkerProcessManager.CreateLanguageWorkerProcess(workerId, runtime, scriptRootPath);
            return new LanguageWorkerChannel(
                         workerId,
                         scriptRootPath,
                         _eventManager,
                         languageWorkerConfig,
                         languageWorkerProcess,
                         workerLogger,
                         _loggerFactory,
                         metricsLogger,
                         attemptCount,
                         managedDependencyOptions);
        }
    }
}
