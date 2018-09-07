// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class SystemLogger : ILogger
    {
        private readonly IEventGenerator _eventGenerator;
        private readonly string _categoryName;
        private readonly string _functionName;
        private readonly bool _isUserFunction;
        private readonly string _hostInstanceId;
        private readonly IEnvironment _environment;

        public SystemLogger(string hostInstanceId, string categoryName, IEventGenerator eventGenerator, IEnvironment environment)
        {
            _environment = environment;
            _eventGenerator = eventGenerator;
            _categoryName = categoryName ?? string.Empty;
            _functionName = LogCategories.IsFunctionCategory(_categoryName) ? _categoryName.Split('.')[1] : string.Empty;
            _isUserFunction = LogCategories.IsFunctionUserCategory(_categoryName);
            _hostInstanceId = hostInstanceId;
        }

        public IDisposable BeginScope<TState>(TState state) => DictionaryLoggerScope.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            // The SystemLogger logs all levels.
            return true;
        }

        private bool IsUserLog<TState>(TState state)
        {
            // User logs are determined by either the category or the presence of the LogPropertyIsUserLogKey
            // in the log state.
            // This check is extra defensive; the 'Function.{FunctionName}.User' category should never occur here
            // as the SystemLoggerProvider checks that before creating a Logger.

            return _isUserFunction ||
                (state is IEnumerable<KeyValuePair<string, object>> stateDict &&
                Utility.GetStateBoolValue(stateDict, ScriptConstants.LogPropertyIsUserLogKey) == true);
        }

        private bool IsDeferredLog(IDictionary<string, object> scopeProps)
        {
            return scopeProps.Keys.Contains(ScriptConstants.LoggerDeferredLog);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            IDictionary<string, object> scopeProps = DictionaryLoggerScope.GetMergedStateDictionary() ?? new Dictionary<string, object>();

            // User logs are not logged to system logs.
            if (!IsEnabled(logLevel) || IsUserLog(state) || IsDeferredLog(scopeProps))
            {
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
            string subscriptionId = _environment.GetSubscriptionId() ?? string.Empty;
            string appName = _environment.GetAzureWebsiteUniqueSlotName() ?? string.Empty;
            string source = _categoryName ?? Utility.GetValueFromState(state, ScriptConstants.LogPropertySourceKey);
            string summary = Sanitizer.Sanitize(formattedMessage) ?? string.Empty;
            string innerExceptionType = string.Empty;
            string innerExceptionMessage = string.Empty;
            string functionName = _functionName;
            string eventName = Utility.GetValueFromState(state, ScriptConstants.LogPropertyEventNameKey);
            string functionInvocationId = Utility.GetValueFromScope(scopeProps, ScriptConstants.LogPropertyFunctionInvocationIdKey) ?? string.Empty;
            string hostInstanceId = _hostInstanceId;
            string activityId = Utility.GetValueFromState(state, ScriptConstants.LogPropertyActivityIdKey);

            // Populate details from the exception.
            string details = string.Empty;
            if (exception != null)
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

            _eventGenerator.LogFunctionTraceEvent(logLevel, subscriptionId, appName, functionName, eventName, source, details, summary, innerExceptionType, innerExceptionMessage, functionInvocationId, hostInstanceId, activityId);
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