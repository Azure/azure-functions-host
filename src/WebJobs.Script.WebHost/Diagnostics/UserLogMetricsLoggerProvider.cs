// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class UserLogMetricsLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly IMetricsLogger _metricsLogger;
        private IExternalScopeProvider _scopeProvider;

        public UserLogMetricsLoggerProvider(IMetricsLogger metricsLogger)
        {
            _metricsLogger = metricsLogger;
        }

        public ILogger CreateLogger(string categoryName)
        {
            if (_scopeProvider == null)
            {
                // Throw a descriptive error if initialization was not performed by the LoggerFactory.
                throw new NullReferenceException($"The {nameof(IExternalScopeProvider)} was not set.");
            }

            return new UserLogMetricsLogger(categoryName, _metricsLogger, _scopeProvider);
        }

        public void Dispose()
        {
        }

        // The default LoggerFactory looks for providers that implement ISupportExternalScope,
        // and will call this method with a shared scope provider.
        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }
    }
}
