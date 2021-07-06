// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reactive.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionMetadataProviderFactory : IFunctionMetadataProviderFactory
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IMetricsLogger _metricsLogger;
        private readonly ILogger _logger;
        private IFunctionMetadataProvider _hostFunctionMetadataProvider;
        private IFunctionMetadataProvider _workerFunctionMetadataProvider;

        public FunctionMetadataProviderFactory(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, ILoggerFactory loggerFactory, IMetricsLogger metricsLogger)
        {
            _applicationHostOptions = applicationHostOptions;
            _metricsLogger = metricsLogger;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
        }

        public void Create()
        {
            _workerFunctionMetadataProvider = new WorkerFunctionMetadataProvider(_applicationHostOptions, _logger, _metricsLogger);

            _hostFunctionMetadataProvider = new HostFunctionMetadataProvider(_applicationHostOptions, _logger, _metricsLogger);
        }

        public IFunctionMetadataProvider GetProvider()
        {
            /*// return host-indexing provider if placeholder mode is enabled or feature flag is disabled
            if (SystemEnvironment.Instance.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime) == "1")
            {
                return _hostFunctionMetadataProvider;
            }
            return _workerFunctionMetadataProvider;*/
            return _hostFunctionMetadataProvider;
        }
    }
}
