// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public class HttpWorkerChannelFactory : IHttpWorkerChannelFactory
    {
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IHttpWorkerProcessFactory _httpWorkerProcessFactory = null;
        private readonly HttpWorkerOptions _httpWorkerOptions = null;
        private readonly IScriptEventManager _eventManager;
        private IHttpWorkerService _httpWorkerService;

        public HttpWorkerChannelFactory(IEnvironment environment, ILoggerFactory loggerFactory, IOptions<HttpWorkerOptions> httpWorkerOptions, IScriptEventManager eventManager,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IHttpWorkerProcessFactory httpWorkerProcessFactory, IHttpWorkerService httpWorkerService)
        {
            _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _httpWorkerOptions = httpWorkerOptions.Value ?? throw new ArgumentNullException(nameof(httpWorkerOptions.Value));
            _httpWorkerProcessFactory = httpWorkerProcessFactory ?? throw new ArgumentNullException(nameof(httpWorkerProcessFactory));
            _httpWorkerService = httpWorkerService ?? throw new ArgumentNullException(nameof(httpWorkerService));
        }

        public IHttpWorkerChannel Create(string scriptRootPath, IMetricsLogger metricsLogger, int attemptCount)
        {
            string workerId = Guid.NewGuid().ToString();
            ILogger workerLogger = _loggerFactory.CreateLogger($"Worker.HttpWorkerChannel.{workerId}");
            IWorkerProcess httpWorkerProcess = _httpWorkerProcessFactory.Create(workerId, scriptRootPath, _httpWorkerOptions);
            return new HttpWorkerChannel(
                         workerId,
                         _eventManager,
                         httpWorkerProcess,
                         _httpWorkerService,
                         workerLogger,
                         metricsLogger,
                         attemptCount);
        }
    }
}
