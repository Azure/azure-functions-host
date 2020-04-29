// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class FunctionInstanceLogCollectorProvider : IEventCollectorProvider
    {
        private readonly IFunctionMetadataManager _metadataManager;
        private readonly IMetricsLogger _metrics;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IConfiguration _configuration;
        private readonly ILoggerFactory _loggerFactory;

        public FunctionInstanceLogCollectorProvider(IFunctionMetadataManager metadataManager,
            IMetricsLogger metrics, IHostIdProvider hostIdProvider, IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _metadataManager = metadataManager ?? throw new ArgumentNullException(nameof(metadataManager));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _hostIdProvider = hostIdProvider ?? throw new ArgumentNullException(nameof(hostIdProvider));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public IAsyncCollector<FunctionInstanceLogEntry> Create()
        {
            return new FunctionInstanceLogger(_metadataManager, _metrics, _hostIdProvider, _configuration, _loggerFactory);
        }
    }
}
