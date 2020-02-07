// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        private string _subscriptionId;
        private string _appName;
        private string _runtimeSiteName;
        private string _slotName;

        public SystemLogger(string hostInstanceId, string categoryName, IEventGenerator eventGenerator, IEnvironment environment,
            IDebugStateProvider debugStateProvider, IScriptEventManager eventManager, IExternalScopeProvider scopeProvider, IOptionsMonitor<StandbyOptions> standbyOptions)
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

            InitializeApplicationInfo();

            if (standbyOptions.CurrentValue.InStandbyMode)
            {
                standbyOptions.OnChange(o =>
                {
                    // we're caching some information that changes when specialization occurs,
                    // so we need to reinitialize
                    InitializeApplicationInfo();
                });
            }
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

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            // User logs are not logged to system logs.
            if (!IsEnabled(logLevel) || IsUserLog(state))
            {
                return;
            }

            // propagate special exceptions through the EventManager
            var stateProps = state as IEnumerable<KeyValuePair<string, object>>;
            string source = _categoryName ?? Utility.GetStateValueOrDefault<string>(stateProps, ScriptConstants.LogPropertySourceKey);
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

            // Apply standard event properties
            // Note: we must be sure to default any null values to empty string
            // otherwise the ETW event will fail to be persisted (silently)
            var scopeProps = _scopeProvider.GetScopeDictionary();
            string summary = formattedMessage ?? string.Empty;
            string functionName = _functionName ?? Utility.ResolveFunctionName(stateProps, scopeProps) ?? string.Empty;
            string eventName = !string.IsNullOrEmpty(eventId.Name) ? eventId.Name : Utility.GetStateValueOrDefault<string>(stateProps, ScriptConstants.LogPropertyEventNameKey) ?? string.Empty;
            string functionInvocationId = Utility.GetValueFromScope(scopeProps, ScriptConstants.LogPropertyFunctionInvocationIdKey) ?? string.Empty;
            string activityId = Utility.GetStateValueOrDefault<string>(stateProps, ScriptConstants.LogPropertyActivityIdKey) ?? string.Empty;

            string innerExceptionType = string.Empty;
            string innerExceptionMessage = string.Empty;
            string details = string.Empty;
            if (exception != null)
            {
                // Populate details from the exception.
                if (string.IsNullOrEmpty(functionName) && exception is FunctionInvocationException fex)
                {
                    functionName = string.IsNullOrEmpty(fex.MethodName) ? string.Empty : fex.MethodName.Replace("Host.Functions.", string.Empty);
                }

                (innerExceptionType, innerExceptionMessage, details) = exception.GetExceptionDetails();
                innerExceptionMessage = innerExceptionMessage ?? string.Empty;
            }

            _eventGenerator.LogFunctionTraceEvent(logLevel, _subscriptionId, _appName, functionName, eventName, source, details, summary, innerExceptionType, innerExceptionMessage, functionInvocationId, _hostInstanceId, activityId, _runtimeSiteName, _slotName);
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

        private void InitializeApplicationInfo()
        {
            _subscriptionId = _environment.GetSubscriptionId() ?? string.Empty;
            _appName = _environment.GetAzureWebsiteUniqueSlotName() ?? string.Empty;
            _runtimeSiteName = _environment.GetRuntimeSiteName() ?? string.Empty;
            _slotName = _environment.GetSlotName() ?? string.Empty;
        }
    }
}