// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class UserLogMetricLoggerTests
    {
        private const string _functionName = "FunctionName";
        private static readonly string _functionCategory = LogCategories.CreateFunctionUserCategory(_functionName);
        private readonly IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

        [Fact]
        public void Log_WritesMetric_ForUserLogs()
        {
            var metrics = new TestMetricsLogger();
            var logger = new UserLogMetricsLogger(_functionCategory, metrics, _scopeProvider);

            // function executions will include this scope
            using (CreateFunctionScope(logger))
            {
                logger.LogInformation("Message");
            }

            var expectedEvent = MetricsEventManager.GetAggregateKey(MetricEventNames.FunctionUserLog, _functionName);
            string eventName = metrics.LoggedEvents.Single();
            Assert.Equal(expectedEvent, eventName);
        }

        [Fact]
        public void Log_WritesMetric_ForUserLogs_WithoutFunctionName()
        {
            var metrics = new TestMetricsLogger();
            var logger = new UserLogMetricsLogger(_functionCategory, metrics, _scopeProvider);

            // If for some reason the scope doesn't exist, we still want this to suceed, but
            // without a function name.
            logger.LogInformation("Message");

            var expectedEvent = MetricsEventManager.GetAggregateKey(MetricEventNames.FunctionUserLog);
            string eventName = metrics.LoggedEvents.Single();
            Assert.Equal(expectedEvent, eventName);
        }

        [Fact]
        public void Log_WritesMetric_IgnoresOtherLogs()
        {
            var metrics = new TestMetricsLogger();
            var logger = new UserLogMetricsLogger(LogCategories.CreateFunctionCategory(_functionName), metrics, _scopeProvider);

            // function executions will include this scope
            using (CreateFunctionScope(logger))
            {
                logger.LogInformation("Message");
            }

            Assert.Empty(metrics.LoggedEvents);
        }

        private IDisposable CreateFunctionScope(ILogger logger)
        {
            return logger.BeginScope(new Dictionary<string, object>
            {
                { ScopeKeys.FunctionName, _functionName }
            });
        }
    }
}
