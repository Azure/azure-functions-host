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
    /// <summary>
    /// Extension methods for use with <see cref="ILogger"/>.
    /// </summary>
    public static class LoggerExtensions
    {
        /// <summary>
        /// Logs a metric value.
        /// </summary>
        /// <param name="logger">The ILogger.</param>
        /// <param name="name">The name of the metric.</param>
        /// <param name="value">The value of the metric.</param>
        /// <param name="properties">Named string values for classifying and filtering metrics.</param>
        public static void LogMetric(this ILogger logger, string name, double value, IDictionary<string, object> properties = null)
        {
            IDictionary<string, object> state = properties == null ? new Dictionary<string, object>() : new Dictionary<string, object>(properties);

            state[LogConstants.NameKey] = name;
            state[LogConstants.MetricValueKey] = value;

            IDictionary<string, object> payload = new ReadOnlyDictionary<string, object>(state);
            logger?.Log(LogLevel.Information, LogConstants.MetricEventId, payload, null, (s, e) => null);
        }

        internal static void LogFunctionResult(this ILogger logger, FunctionInstanceLogEntry logEntry)
        {
            bool succeeded = logEntry.Exception == null;

            // build the string and values
            string result = succeeded ? "Succeeded" : "Failed";
            string logString = $"Executed '{{{LogConstants.FullNameKey}}}' ({result}, Id={{{LogConstants.InvocationIdKey}}})";
            object[] values = new object[]
            {
                logEntry.FunctionName,
                logEntry.FunctionInstanceId
            };

            // generate additional payload that is not in the string
            IDictionary<string, object> properties = new Dictionary<string, object>();
            properties.Add(LogConstants.NameKey, logEntry.LogName);
            properties.Add(LogConstants.TriggerReasonKey, logEntry.TriggerReason);
            properties.Add(LogConstants.StartTimeKey, logEntry.StartTime);
            properties.Add(LogConstants.EndTimeKey, logEntry.EndTime);
            properties.Add(LogConstants.DurationKey, logEntry.Duration);
            properties.Add(LogConstants.SucceededKey, succeeded);

            if (logEntry.Arguments != null)
            {
                foreach (var arg in logEntry.Arguments)
                {
                    properties.Add(LogConstants.ParameterPrefix + arg.Key, arg.Value);
                }
            }

            FormattedLogValuesCollection payload = new FormattedLogValuesCollection(logString, values, new ReadOnlyDictionary<string, object>(properties));
            LogLevel level = succeeded ? LogLevel.Information : LogLevel.Error;
            logger.Log(level, 0, payload, logEntry.Exception, (s, e) => s.ToString());
        }

        internal static void LogFunctionResultAggregate(this ILogger logger, FunctionResultAggregate resultAggregate)
        {
            // we won't output any string here, just the data
            logger.Log(LogLevel.Information, 0, resultAggregate.ToReadOnlyDictionary(), null, (s, e) => null);
        }

        internal static IDisposable BeginFunctionScope(this ILogger logger, IFunctionInstance functionInstance)
        {
            return logger?.BeginScope(
                new Dictionary<string, object>
                {
                    [ScopeKeys.FunctionInvocationId] = functionInstance?.Id.ToString(),
                    [ScopeKeys.FunctionName] = functionInstance?.FunctionDescriptor?.LogName,
                    [ScopeKeys.Event] = LogConstants.FunctionStartEvent
                });
        }
    }
}
