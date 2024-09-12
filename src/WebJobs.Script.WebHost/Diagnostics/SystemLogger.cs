// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors.Internal;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class SystemLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _functionName;
        private readonly string _hostInstanceId;
        private readonly bool _isUserFunction;
        private readonly LogLevel _logLevel;
        private readonly IEnvironment _environment;
        private readonly IEventGenerator _eventGenerator;
        private readonly IDebugStateProvider _debugStateProvider;
        private readonly IScriptEventManager _eventManager;
        private readonly IExternalScopeProvider _scopeProvider;
        private AppServiceOptions _appServiceOptions;

        public SystemLogger(string hostInstanceId, string categoryName, IEventGenerator eventGenerator, IEnvironment environment,  IDebugStateProvider debugStateProvider,
           IScriptEventManager eventManager, IExternalScopeProvider scopeProvider, IOptionsMonitor<AppServiceOptions> appServiceOptionsMonitor)
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

            appServiceOptionsMonitor.OnChange(newOptions => _appServiceOptions = newOptions);
            _appServiceOptions = appServiceOptionsMonitor.CurrentValue;
        }

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            // When in diagnostic mode, we log everything, but that has a .UtcNow check,
            // so first see if we even need to make that assessment.
            return logLevel >= _logLevel || _debugStateProvider.InDiagnosticMode;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel) || _isUserFunction || FunctionInvoker.CurrentScope == FunctionInvocationScope.User)
            {
                return;
            }

            // Enumerate all the state values once, capturing the values we'll use below - last one wins.
            string stateSourceValue = null;
            string stateFunctionName = null;
            string stateEventName = null;
            string stateActivityId = null;
            string diagnosticEventErrorCode = null;
            bool isDiagnosticEvent = false;
            if (state is IEnumerable<KeyValuePair<string, object>> stateProps)
            {
                foreach (var kvp in stateProps)
                {
                    if (string.Equals(kvp.Key, ScriptConstants.LogPropertySourceKey, StringComparison.OrdinalIgnoreCase))
                    {
                        stateSourceValue = kvp.Value?.ToString();
                    }
                    else if (string.Equals(kvp.Key, ScriptConstants.DiagnosticEventKey, StringComparison.OrdinalIgnoreCase))
                    {
                        isDiagnosticEvent = true;
                    }
                    else if (string.Equals(kvp.Key, ScriptConstants.ErrorCodeKey, StringComparison.OrdinalIgnoreCase))
                    {
                        diagnosticEventErrorCode = kvp.Value?.ToString();
                    }
                    else if (string.Equals(kvp.Key, ScriptConstants.LogPropertyIsUserLogKey, StringComparison.OrdinalIgnoreCase))
                    {
                        if ((bool)kvp.Value)
                        {
                            return;
                        }
                    }
                    else if (Utility.IsFunctionName(kvp))
                    {
                        stateFunctionName = kvp.Value?.ToString();
                    }
                    else if (string.Equals(kvp.Key, ScriptConstants.LogPropertyEventNameKey, StringComparison.OrdinalIgnoreCase))
                    {
                        stateEventName = kvp.Value?.ToString();
                    }
                    else if (string.Equals(kvp.Key, ScriptConstants.LogPropertyActivityIdKey, StringComparison.OrdinalIgnoreCase))
                    {
                        stateActivityId = kvp.Value?.ToString();
                    }
                }
            }

            // Propagate special exceptions through the EventManager.
            string source = _categoryName ?? stateSourceValue;
            if (exception is FunctionIndexingException && _eventManager != null)
            {
                _eventManager.Publish(new FunctionIndexingEvent(nameof(FunctionIndexingException), source, exception));
            }

            // If we don't have a message, there's nothing to log.
            string formattedMessage = formatter?.Invoke(state, exception);
            if (string.IsNullOrEmpty(formattedMessage))
            {
                return;
            }

            var scopeProps = _scopeProvider.GetScopeDictionaryOrNull();
            string functionName = _functionName ?? stateFunctionName ?? string.Empty;
            if (string.IsNullOrEmpty(functionName) && scopeProps?.Count > 0)
            {
                if (Utility.TryGetFunctionName(scopeProps, out string scopeFunctionName))
                {
                    functionName = scopeFunctionName;
                }
            }

            string invocationId = string.Empty;
            object scopeValue = null;
            string scopeActivityId = null;
            if (scopeProps != null)
            {
                if (scopeProps.TryGetValue(ScriptConstants.LogPropertyFunctionInvocationIdKey, out scopeValue) && scopeValue != null)
                {
                    invocationId = scopeValue.ToString();
                }

                // For Http function invocations we want to stamp invocation logs with
                // the request ID for easy correlation with incoming Http request logs.
                if (scopeProps.TryGetValue(ScriptConstants.AzureFunctionsRequestIdKey, out scopeValue))
                {
                    scopeActivityId = scopeValue as string;
                }
            }

            // Apply standard event properties.
            // Note: we must be sure to default any null values to empty string
            // otherwise the ETW event will fail to be persisted (silently).
            string summary = Sanitizer.Sanitize(formattedMessage) ?? string.Empty;
            string eventName = !string.IsNullOrEmpty(eventId.Name) ? eventId.Name : stateEventName ?? string.Empty;
            eventName = isDiagnosticEvent ? $"DiagnosticEvent-{diagnosticEventErrorCode}" : eventName;

            string activityId = stateActivityId ?? scopeActivityId ?? string.Empty;
            var options = _appServiceOptions;
            string subscriptionId = options.SubscriptionId ?? string.Empty;
            string appName = options.AppName ?? string.Empty;
            string runtimeSiteName = options.RuntimeSiteName ?? string.Empty;
            string slotName = options.SlotName ?? string.Empty;

            string innerExceptionType = string.Empty;
            string innerExceptionMessage = string.Empty;
            string details = string.Empty;
            if (exception != null)
            {
                // Populate details from the exception.
                if (string.IsNullOrEmpty(functionName) && exception is FunctionException fex)
                {
                    functionName = string.IsNullOrEmpty(fex.MethodName) ? string.Empty : fex.MethodName.Replace("Host.Functions.", string.Empty);
                }

                (innerExceptionType, innerExceptionMessage, details) = exception.GetExceptionDetails();
                formattedMessage = Sanitizer.Sanitize(formattedMessage);
                innerExceptionMessage = Sanitizer.Sanitize(innerExceptionMessage) ?? string.Empty;
            }

            _eventGenerator.LogFunctionTraceEvent(logLevel, subscriptionId, appName, functionName, eventName, source, details, summary, innerExceptionType, innerExceptionMessage, invocationId, _hostInstanceId, activityId, runtimeSiteName, slotName, DateTime.UtcNow);
        }
    }
}