// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class UserLogMetricsLogger : ILogger
    {
        private readonly string _category;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IExternalScopeProvider _scopeProvider;

        public UserLogMetricsLogger(string category, IMetricsLogger metricsLogger, IExternalScopeProvider scopeProvider)
        {
            _category = category ?? throw new ArgumentNullException(nameof(category));
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
        }

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            return LogCategories.IsFunctionUserCategory(_category);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var scope = _scopeProvider.GetScopeDictionary();
            scope.TryGetValue(ScopeKeys.FunctionName, out string functionName);

            _metricsLogger.LogEvent(MetricEventNames.FunctionUserLog, functionName);
        }
    }
}
