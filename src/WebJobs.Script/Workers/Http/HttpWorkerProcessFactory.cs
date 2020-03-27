﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    internal class HttpWorkerProcessFactory : IHttpWorkerProcessFactory
    {
        private readonly IWorkerProcessFactory _workerProcessFactory;
        private readonly IProcessRegistry _processRegistry;
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IScriptEventManager _eventManager = null;
        private readonly IWorkerConsoleLogSource _consoleLogSource;
        private readonly IEnvironment _environment;

        public HttpWorkerProcessFactory(IScriptEventManager eventManager,
                                       ILoggerFactory loggerFactory,
                                       IWorkerProcessFactory defaultWorkerProcessFactory,
                                       IProcessRegistry processRegistry,
                                       IWorkerConsoleLogSource consoleLogSource,
                                       IEnvironment environment)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
            _consoleLogSource = consoleLogSource ?? throw new ArgumentNullException(nameof(consoleLogSource));
            _workerProcessFactory = defaultWorkerProcessFactory ?? throw new ArgumentNullException(nameof(defaultWorkerProcessFactory));
            _processRegistry = processRegistry ?? throw new ArgumentNullException(nameof(processRegistry));
            _environment = environment;
        }

        public IWorkerProcess Create(string workerId, string scriptRootPath, HttpWorkerOptions httpWorkerOptions)
        {
            ILogger workerProcessLogger = _loggerFactory.CreateLogger($"Worker.HttpWorkerProcess.{workerId}");
            return new HttpWorkerProcess(workerId, scriptRootPath, httpWorkerOptions, _eventManager, _workerProcessFactory, _processRegistry, workerProcessLogger, _consoleLogSource, _environment);
        }
    }
}
