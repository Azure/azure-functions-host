// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.OutOfProc.Http;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc
{
    public class HttpWorkerChannelFactory : IHttpWorkerChannelFactory
    {
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IHttpWorkerProcessFactory _httpInvokerProcessFactory = null;
        private readonly HttpWorkerOptions _httpInvokerOptions = null;
        private IHttpInvokerService _httpInvokerService;

        public HttpWorkerChannelFactory(IEnvironment environment, ILoggerFactory loggerFactory, IOptions<HttpWorkerOptions> httpInvokerOptions,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IHttpWorkerProcessFactory httpInvokerProcessFactory, IHttpInvokerService httpInvokerService)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _httpInvokerOptions = httpInvokerOptions.Value ?? throw new ArgumentNullException(nameof(httpInvokerOptions.Value));
            _httpInvokerProcessFactory = httpInvokerProcessFactory ?? throw new ArgumentNullException(nameof(httpInvokerProcessFactory));
            _httpInvokerService = httpInvokerService ?? throw new ArgumentNullException(nameof(httpInvokerService));
        }

        public IHttpWorkerChannel Create(string scriptRootPath, IMetricsLogger metricsLogger, int attemptCount)
        {
            string workerId = Guid.NewGuid().ToString();
            ILogger workerLogger = _loggerFactory.CreateLogger($"Worker.HttpInvokerChannel.{workerId}");
            ILanguageWorkerProcess httpWorkerProcess = _httpInvokerProcessFactory.Create(workerId, scriptRootPath, _httpInvokerOptions.Arguments);
            return new HttpWorkerChannel(
                         workerId,
                         httpWorkerProcess,
                         _httpInvokerService,
                         workerLogger,
                         metricsLogger,
                         attemptCount);
        }
    }
}
