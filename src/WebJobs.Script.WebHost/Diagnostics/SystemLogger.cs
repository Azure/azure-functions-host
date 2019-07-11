// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class SystemLogger : ILogger
    {
        private readonly IEventGenerator _eventGenerator;
        private readonly string _categoryName;
        private readonly string _functionName;
        private readonly bool _isUserFunction;
        private readonly bool _isAllowedCategory;
        private readonly string _hostInstanceId;
        private readonly IEnvironment _environment;
        private readonly LogLevel _logLevel;
        private readonly IDebugStateProvider _debugStateProvider;
        private readonly IScriptEventManager _eventManager;
        private readonly IExternalScopeProvider _scopeProvider;

        private static readonly string[] _allowedSystemCategories = new string[]
        {
            LogCategories.Startup,
            LogCategories.Singleton,
            LogCategories.Executor,
            LogCategories.Bindings,
            ScriptConstants.LogCategoryFunctionsController,
            ScriptConstants.LogCategoryHostController,
            ScriptConstants.LogCategoryHostGeneral,
            ScriptConstants.LogCategoryHostMetrics,
            ScriptConstants.LogCategoryInstanceController,
            ScriptConstants.LogCategoryKeysController,
            ScriptConstants.LogCategoryFileWatcher,
            ScriptConstants.LogCategoryLanguageWorkerConfig,
            ScriptConstants.LogCategoryHttpThrottleMiddleware,
            LogCategories.CreateTriggerCategory("CosmosDB"),
            LogCategories.CreateTriggerCategory("DurableTask"),
            LogCategories.CreateTriggerCategory("EventGrid"),
            LogCategories.CreateTriggerCategory("EventHub"),
            LogCategories.CreateTriggerCategory("Queue"),
            LogCategories.CreateTriggerCategory("Timer")
    };

        public SystemLogger(string hostInstanceId, string categoryName, IEventGenerator eventGenerator, IEnvironment environment,
            IDebugStateProvider debugStateProvider, IScriptEventManager eventManager, IExternalScopeProvider scopeProvider)
        {
            _environment = environment;
            _eventGenerator = eventGenerator;
            _categoryName = categoryName ?? string.Empty;
            _logLevel = LogLevel.Debug;
            _functionName = LogCategories.IsFunctionCategory(_categoryName) ? _categoryName.Split('.')[1] : string.Empty;
            _isUserFunction = LogCategories.IsFunctionUserCategory(_categoryName);
            _hostInstanceId = hostInstanceId;
            _debugStateProvider = debugStateProvider;
            _eventManager = eventManager;
            _isAllowedCategory = IsAllowedCategory(_categoryName);
            _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
        }

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

        // All of the pre-created log categories are accounted for here. Loggers created with Logger<T>
        // will be handled in the call to Log()
        internal static bool IsAllowedCategory(string category)
        {
            return _allowedSystemCategories.Contains(category) ||
                LogCategories.IsFunctionCategory(category) ||
                LanguageWorkerConstants.IsLanguageWorkerLogCategory(category);
        }

        // All of this logic could be done in a logging filter, but we do it here to prevent any manipulation
        public bool IsEnabled(LogLevel logLevel)
        {
            if (!_isAllowedCategory && !IsSystemLog())
            {
                return false;
            }

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

        private bool IsSystemLog()
        {
            bool isSystemLog = false;

            // If we find the key anywhere in the scope, return true
            _scopeProvider.ForEachScope<object>((scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> kvps &&
                    kvps.Any(p => p.Key == ScriptConstants.LogPropertyIsSystemLogKey))
                {
                    isSystemLog = true;
                }
            }, null);

            return isSystemLog;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            // propagate special exceptions through the EventManager
            string source = _categoryName ?? Utility.GetValueFromState(state, ScriptConstants.LogPropertySourceKey);
            if (exception is FunctionIndexingException && _eventManager != null)
            {
                _eventManager.Publish(new FunctionIndexingEvent("FunctionIndexingException", source, exception));
            }

            if (!IsEnabled(logLevel) || IsUserLog(state))
            {
                return;
            }

            IDictionary<string, object> scopeProps = _scopeProvider.GetScopeDictionary() ?? new Dictionary<string, object>();

            // If the _functionName does not match the scope, skip it.
            if (!string.IsNullOrEmpty(_functionName) &&
                scopeProps.TryGetValue(ScopeKeys.FunctionName, out object functionNameFromScope) &&
                string.Compare(functionNameFromScope?.ToString(), _functionName, ignoreCase: false) != 0)
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
            string summary = Sanitizer.Sanitize(formattedMessage) ?? string.Empty;
            string innerExceptionType = string.Empty;
            string innerExceptionMessage = string.Empty;
            string functionName = _functionName;
            string eventName = !string.IsNullOrEmpty(eventId.Name) ? eventId.Name : Utility.GetValueFromState(state, ScriptConstants.LogPropertyEventNameKey);
            string functionInvocationId = Utility.GetValueFromScope(scopeProps, ScriptConstants.LogPropertyFunctionInvocationIdKey) ?? string.Empty;
            string hostInstanceId = _hostInstanceId;
            string activityId = Utility.GetValueFromState(state, ScriptConstants.LogPropertyActivityIdKey);

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

            _eventGenerator.LogFunctionTraceEvent(logLevel, subscriptionId, appName, functionName, eventName, source, details, summary, innerExceptionType, innerExceptionMessage, functionInvocationId, hostInstanceId, activityId);
        }
    }
}