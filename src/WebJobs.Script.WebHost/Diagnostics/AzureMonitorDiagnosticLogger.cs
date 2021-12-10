// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class AzureMonitorDiagnosticLogger : ILogger
    {
        internal const string AzureMonitorCategoryName = "FunctionAppLogs";
        internal const string AzureMonitorOperationName = "Microsoft.Web/sites/functions/log";

        private static int _processId = Process.GetCurrentProcess().Id;

        private readonly string _hostVersion = ScriptHost.Version;
        private readonly string _regionName;
        private readonly string _category;
        private readonly string _hostInstanceId;
        private readonly string _roleInstance;
        private readonly HostNameProvider _hostNameProvider;
        private readonly IEventGenerator _eventGenerator;
        private readonly IEnvironment _environment;
        private readonly IExternalScopeProvider _scopeProvider;
        private AppServiceOptions _appServiceOptions;

        public AzureMonitorDiagnosticLogger(string category, string hostInstanceId, IEventGenerator eventGenerator, IEnvironment environment, IExternalScopeProvider scopeProvider,
            HostNameProvider hostNameProvider, IOptionsMonitor<AppServiceOptions> appServiceOptionsMonitor)
        {
            _category = category ?? throw new ArgumentNullException(nameof(category));
            _hostInstanceId = hostInstanceId ?? throw new ArgumentNullException(nameof(hostInstanceId));
            _eventGenerator = eventGenerator ?? throw new ArgumentNullException(nameof(eventGenerator));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
            _hostNameProvider = hostNameProvider ?? throw new ArgumentNullException(nameof(hostNameProvider));
            _ = appServiceOptionsMonitor ?? throw new ArgumentNullException(nameof(appServiceOptionsMonitor));

            appServiceOptionsMonitor.OnChange(newOptions => _appServiceOptions = newOptions);
            _appServiceOptions = appServiceOptionsMonitor.CurrentValue;

            _roleInstance = _environment.GetInstanceId();

            _regionName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.RegionName) ?? string.Empty;
        }

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            // We want to instantiate this Logger in placeholder mode to warm it up, but do not want to log anything.
            return !string.IsNullOrEmpty(_hostNameProvider.Value) && !_environment.IsPlaceholderModeEnabled();
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            // Make sure we have something to log
            string formattedMessage = formatter?.Invoke(state, exception);
            if (string.IsNullOrEmpty(formattedMessage) && exception == null)
            {
                return;
            }

            (string exceptionType, string exceptionMessage, string exceptionDetails) = exception.GetExceptionDetails();
            if (exception != null)
            {
                formattedMessage = Sanitizer.Sanitize(formattedMessage);
            }

            // enumerate all the state values once, capturing the values we'll use below
            // last one wins
            string stateFunctionName = null;
            if (state is IEnumerable<KeyValuePair<string, object>> stateProps)
            {
                foreach (var kvp in stateProps)
                {
                    if (Utility.IsFunctionName(kvp))
                    {
                        stateFunctionName = kvp.Value?.ToString();
                    }
                }
            }

            var scopeProps = _scopeProvider.GetScopeDictionaryOrNull();
            string functionName = stateFunctionName;
            if (string.IsNullOrEmpty(functionName))
            {
                if (Utility.TryGetFunctionName(scopeProps, out string scopeFunctionName))
                {
                    functionName = scopeFunctionName;
                }
            }

            // Build up a JSON string for the Azure Monitor 'properties' bag
            StringWriter sw = new StringWriter();
            using (JsonTextWriter writer = new JsonTextWriter(sw) { Formatting = Formatting.None })
            {
                writer.WriteStartObject();
                WritePropertyIfNotNull(writer, "appName", _appServiceOptions.AppName);
                WritePropertyIfNotNull(writer, "roleInstance", _roleInstance);
                WritePropertyIfNotNull(writer, "message", formattedMessage);
                WritePropertyIfNotNull(writer, "category", _category);
                WritePropertyIfNotNull(writer, "hostVersion", _hostVersion);
                WritePropertyIfNotNull(writer, "functionInvocationId", Utility.GetValueFromScope(scopeProps, ScopeKeys.FunctionInvocationId));
                WritePropertyIfNotNull(writer, "functionName", functionName);
                WritePropertyIfNotNull(writer, "hostInstanceId", _hostInstanceId);
                WritePropertyIfNotNull(writer, "activityId", Utility.GetValueFromScope(scopeProps, ScriptConstants.LogPropertyActivityIdKey));
                WritePropertyIfNotNull(writer, "level", logLevel.ToString());
                WritePropertyIfNotNull(writer, "levelId", (int)logLevel);
                WritePropertyIfNotNull(writer, "processId", _processId);
                WritePropertyIfNotNull(writer, nameof(exceptionDetails), exceptionDetails);
                WritePropertyIfNotNull(writer, nameof(exceptionMessage), exceptionMessage);
                WritePropertyIfNotNull(writer, nameof(exceptionType), exceptionType);

                // Only write the event if it's relevant
                if (eventId.Id != 0 || !string.IsNullOrEmpty(eventId.Name))
                {
                    WriteProperty(writer, "eventId", eventId.Id);
                    WriteProperty(writer, "eventName", eventId.Name);
                }

                writer.WriteEndObject();
            }

            _eventGenerator.LogAzureMonitorDiagnosticLogEvent(logLevel, _hostNameProvider.Value, AzureMonitorOperationName, AzureMonitorCategoryName, _regionName, sw.ToString());
        }

        private static void WritePropertyIfNotNull<T>(JsonTextWriter writer, string propertyName, T propertyValue)
        {
            if (propertyValue != null)
            {
                WriteProperty(writer, propertyName, propertyValue);
            }
        }

        private static void WriteProperty<T>(JsonTextWriter writer, string propertyName, T propertyValue)
        {
            writer.WritePropertyName(propertyName);
            writer.WriteValue(propertyValue);
        }
    }
}
