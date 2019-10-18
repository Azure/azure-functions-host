// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Eventing;
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
        private readonly LogLevel _logLevel;
        private readonly IDebugStateProvider _debugStateProvider;
        private readonly IScriptEventManager _eventManager;
        private readonly IExternalScopeProvider _scopeProvider;

        public SystemLogger(string hostInstanceId, string categoryName, IEventGenerator eventGenerator, IEnvironment environment,
            IDebugStateProvider debugStateProvider, IScriptEventManager eventManager, IExternalScopeProvider scopeProvider)
        {
            _environment = environment;
            _eventGenerator = eventGenerator;
            _categoryName = categoryName ?? string.Empty;
            _logLevel = LogLevel.Debug;
            _functionName = LogCategories.IsFunctionCategory(_categoryName) ? _categoryName.Split('.')[1] : null;
            _isUserFunction = LogCategories.IsFunctionUserCategory(_categoryName);
            _hostInstanceId = hostInstanceId;
            _debugStateProvider = debugStateProvider;
            _eventManager = eventManager;
            _scopeProvider = scopeProvider;
        }

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            if (_debugStateProvider.InDiagnosticMode)
            {
                // when in diagnostic mode, we log everything
                return true;
            }
            return logLevel >= _logLevel;
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

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            // propagate special exceptions through the EventManager
            var stateProps = state as IEnumerable<KeyValuePair<string, object>> ?? new Dictionary<string, object>();

            string source = _categoryName ?? Utility.GetStateValueOrDefault<string>(stateProps, ScriptConstants.LogPropertySourceKey);
            if (exception is FunctionIndexingException && _eventManager != null)
            {
                _eventManager.Publish(new FunctionIndexingEvent("FunctionIndexingException", source, exception));
            }

            // User logs are not logged to system logs.
            if (!IsEnabled(logLevel) || IsUserLog(state))
            {
                return;
            }

            string formattedMessage = formatter?.Invoke(state, exception);

            // If we don't have a message, there's nothing to log.
            if (string.IsNullOrEmpty(formattedMessage))
            {
                return;
            }

            IDictionary<string, object> scopeProps = _scopeProvider.GetScopeDictionary();

            // Apply standard event properties
            // Note: we must be sure to default any null values to empty string
            // otherwise the ETW event will fail to be persisted (silently)
            string subscriptionId = _environment.GetSubscriptionId() ?? string.Empty;
            string appName = _environment.GetAzureWebsiteUniqueSlotName() ?? string.Empty;
            string summary = Sanitizer.Sanitize(formattedMessage) ?? string.Empty;
            string innerExceptionType = string.Empty;
            string innerExceptionMessage = string.Empty;
            string functionName = _functionName ?? Utility.ResolveFunctionName(stateProps, scopeProps) ?? string.Empty;
            string eventName = !string.IsNullOrEmpty(eventId.Name) ? eventId.Name : Utility.GetStateValueOrDefault<string>(stateProps, ScriptConstants.LogPropertyEventNameKey) ?? string.Empty;
            string functionInvocationId = Utility.GetValueFromScope(scopeProps, ScriptConstants.LogPropertyFunctionInvocationIdKey) ?? string.Empty;
            string hostInstanceId = _hostInstanceId;
            string activityId = Utility.GetStateValueOrDefault<string>(stateProps, ScriptConstants.LogPropertyActivityIdKey) ?? string.Empty;
            string runtimeSiteName = _environment.GetRuntimeSiteName() ?? string.Empty;

            // Populate details from the exception.
            string details = string.Empty;
            if (exception != null)
            {
                if (string.IsNullOrEmpty(functionName) && exception is FunctionInvocationException fex)
                {
                    functionName = string.IsNullOrEmpty(fex.MethodName) ? string.Empty : fex.MethodName.Replace("Host.Functions.", string.Empty);
                }

                (innerExceptionType, innerExceptionMessage, details) = exception.GetExceptionDetails();
                innerExceptionMessage = innerExceptionMessage ?? string.Empty;
            }

            _eventGenerator.LogFunctionTraceEvent(logLevel, subscriptionId, appName, functionName, eventName, source, details, summary, innerExceptionType, innerExceptionMessage, functionInvocationId, hostInstanceId, activityId, runtimeSiteName);
        }
    }
}