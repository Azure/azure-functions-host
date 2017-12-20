// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class SystemLogger : ILogger
    {
        private readonly IEventGenerator _eventGenerator;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly string _appName;
        private readonly string _subscriptionId;
        private readonly string _categoryName;
        private readonly string _functionName;

        public SystemLogger(string categoryName, IEventGenerator eventGenerator, ScriptSettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
            _appName = _settingsManager.AzureWebsiteUniqueSlotName;
            _subscriptionId = Utility.GetSubscriptionId();
            _eventGenerator = eventGenerator;
            _categoryName = categoryName;
            _functionName = LogCategories.IsFunctionCategory(_categoryName) ? _categoryName.Split('.')[1] : string.Empty;
        }

        public IDisposable BeginScope<TState>(TState state) => DictionaryLoggerScope.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            // The SystemLogger logs all levels.
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            if (state is IDictionary<string, object> stateDict &&
                stateDict.TryGetValue(ScriptConstants.LogPropertyIsUserLogKey, out object value) &&
                value != null && value is bool && (bool)value == true)
            {
                // we don't write user traces to system logs
                return;
            }

            string formattedMessage = formatter?.Invoke(state, exception);

            // If we don't have a message, there's nothing to log.
            if (string.IsNullOrEmpty(formattedMessage))
            {
                return;
            }

            // Apply standard event properties
            // Note: we must be sure to default any null values to empty string
            // otherwise the ETW event will fail to be persisted (silently)
            string subscriptionId = _subscriptionId ?? string.Empty;
            string appName = _appName ?? string.Empty;
            string source = _categoryName ?? string.Empty;
            string summary = Sanitizer.Sanitize(formattedMessage) ?? string.Empty;
            string innerExceptionType = string.Empty;
            string innerExceptionMessage = string.Empty;
            string functionName = _functionName;

            // eventName is not currently used
            string eventName = string.Empty;

            // Populate details from the exception.
            string details = string.Empty;
            if (string.IsNullOrEmpty(details) && exception != null)
            {
                details = Sanitizer.Sanitize(exception.ToFormattedString());

                if (string.IsNullOrEmpty(functionName) && exception is FunctionInvocationException fex)
                {
                    functionName = string.IsNullOrEmpty(fex.MethodName) ? string.Empty : fex.MethodName.Replace("Host.Functions.", string.Empty);
                }

                Exception innerException = exception.InnerException;
                while (innerException != null && innerException.InnerException != null)
                {
                    innerException = innerException.InnerException;
                }

                if (innerException != null)
                {
                    GetExceptionDetails(innerException, out innerExceptionType, out innerExceptionMessage);
                }
                else
                {
                    GetExceptionDetails(exception, out innerExceptionType, out innerExceptionMessage);
                }
            }

            _eventGenerator.LogFunctionTraceEvent(logLevel, subscriptionId, appName, functionName, eventName, source, details, summary, innerExceptionType, innerExceptionMessage);
        }

        private void GetExceptionDetails(Exception exception, out string exceptionType, out string exceptionMessage)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }
            exceptionType = exception.GetType().ToString();
            exceptionMessage = Sanitizer.Sanitize(exception.Message) ?? string.Empty;
        }
    }
}