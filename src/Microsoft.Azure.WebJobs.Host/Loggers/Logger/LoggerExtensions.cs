// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Logging;

namespace Microsoft.Extensions.Logging
{
    internal static class LoggerExtensions
    {
        // We want the short name for use with Application Insights.
        internal static void LogFunctionResult(this ILogger logger, string shortName, FunctionInstanceLogEntry logEntry, TimeSpan duration, Exception exception = null)
        {
            bool succeeded = exception == null;

            // build the string and values
            string result = succeeded ? "Succeeded" : "Failed";
            string logString = $"Executed '{{{LoggingKeys.FullName}}}' ({result}, Id={{{LoggingKeys.InvocationId}}})";
            object[] values = new object[]
            {
                logEntry.FunctionName,
                logEntry.FunctionInstanceId
            };

            // generate additional payload that is not in the string
            IDictionary<string, object> properties = new Dictionary<string, object>();
            properties.Add(LoggingKeys.Name, shortName);
            properties.Add(LoggingKeys.TriggerReason, logEntry.TriggerReason);
            properties.Add(LoggingKeys.StartTime, logEntry.StartTime);
            properties.Add(LoggingKeys.EndTime, logEntry.EndTime);
            properties.Add(LoggingKeys.Duration, duration);
            properties.Add(LoggingKeys.Succeeded, succeeded);

            if (logEntry.Arguments != null)
            {
                foreach (var arg in logEntry.Arguments)
                {
                    properties.Add(LoggingKeys.ParameterPrefix + arg.Key, arg.Value);
                }
            }

            FormattedLogValuesCollection payload = new FormattedLogValuesCollection(logString, values, new ReadOnlyDictionary<string, object>(properties));
            LogLevel level = succeeded ? LogLevel.Information : LogLevel.Error;
            logger.Log(level, 0, payload, exception, (s, e) => s.ToString());
        }

        internal static void LogFunctionResultAggregate(this ILogger logger, FunctionResultAggregate resultAggregate)
        {
            // we won't output any string here, just the data
            FormattedLogValuesCollection payload = new FormattedLogValuesCollection(string.Empty, null, resultAggregate.ToReadOnlyDictionary());
            logger.Log(LogLevel.Information, 0, payload, null, (s, e) => s.ToString());
        }

        internal static IDisposable BeginFunctionScope(this ILogger logger, IFunctionInstance functionInstance)
        {
            return logger?.BeginScope(
                new Dictionary<string, object>
                {
                    [ScopeKeys.FunctionInvocationId] = functionInstance?.Id,
                    [ScopeKeys.FunctionName] = functionInstance?.FunctionDescriptor?.LogName
                });
        }
    }
}
