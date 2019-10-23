// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.OutOfProc.Http;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc
{
    public class HttpWorkerChannelFactory : IHttpWorkerChannelFactory
    {
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IHttpWorkerProcessFactory _httpWorkerProcessFactory = null;
        private readonly HttpWorkerOptions _httpWorkerOptions = null;
        private IHttpWorkerService _httpWorkerService;

        public HttpWorkerChannelFactory(IEnvironment environment, ILoggerFactory loggerFactory, IOptions<HttpWorkerOptions> httpWorkerOptions,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IHttpWorkerProcessFactory httpWorkerProcessFactory, IHttpWorkerService httpWorkerService)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _httpWorkerOptions = httpWorkerOptions.Value ?? throw new ArgumentNullException(nameof(httpWorkerOptions.Value));
            _httpWorkerProcessFactory = httpWorkerProcessFactory ?? throw new ArgumentNullException(nameof(httpWorkerProcessFactory));
            _httpWorkerService = httpWorkerService ?? throw new ArgumentNullException(nameof(httpWorkerService));
        }

        public IHttpWorkerChannel Create(string scriptRootPath, IMetricsLogger metricsLogger, int attemptCount)
        {
            string workerId = Guid.NewGuid().ToString();
            ILogger workerLogger = _loggerFactory.CreateLogger($"Worker.HttpWorkerChannel.{workerId}");
            ILanguageWorkerProcess httpWorkerProcess = _httpWorkerProcessFactory.Create(workerId, scriptRootPath, _httpWorkerOptions);
            return new HttpWorkerChannel(
                         workerId,
                         httpWorkerProcess,
                         _httpWorkerService,
                         workerLogger,
                         metricsLogger,
                         attemptCount);
        }
    }
}
