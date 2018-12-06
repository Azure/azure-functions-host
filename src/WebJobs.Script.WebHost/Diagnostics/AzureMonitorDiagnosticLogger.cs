// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class AzureMonitorDiagnosticLogger : ILogger
    {
        internal const string AzureMonitorCategoryName = "FunctionExecutionLogs";
        internal const string AzureMonitorOperationName = "Microsoft.Web/sites/functions/execution";

        private readonly string _hostVersion = ScriptHost.Version;
        private readonly string _regionName;
        private readonly string _category;
        private readonly string _hostInstanceId;
        private readonly string _websiteHostName;

        private readonly IEventGenerator _eventGenerator;
        private readonly IEnvironment _environment;
        private readonly IExternalScopeProvider _scopeProvider;

        public AzureMonitorDiagnosticLogger(string category, string hostInstanceId, IEventGenerator eventGenerator, IEnvironment environment, IExternalScopeProvider scopeProvider)
        {
            _category = category ?? throw new ArgumentNullException(nameof(category));
            _hostInstanceId = hostInstanceId ?? throw new ArgumentNullException(nameof(hostInstanceId));
            _eventGenerator = eventGenerator ?? throw new ArgumentNullException(nameof(eventGenerator));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));

            _regionName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.RegionName) ?? string.Empty;
            _websiteHostName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName);
        }

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string formattedMessage = formatter?.Invoke(state, exception);

            // Make sure we have something to log
            if (string.IsNullOrEmpty(formattedMessage) && exception == null)
            {
                return;
            }

            (string exceptionType, string exceptionMessage, string exceptionDetails) = exception.GetExceptionDetails();

            var scopeProps = _scopeProvider.GetScopeDictionary();

            // Build up a JSON string for the Azure Monitor 'properties' bag
            StringWriter sw = new StringWriter();
            using (JsonTextWriter writer = new JsonTextWriter(sw) { Formatting = Formatting.None })
            {
                writer.WriteStartObject();
                WritePropertyIfNotNull(writer, "message", Sanitizer.Sanitize(formattedMessage));
                WritePropertyIfNotNull(writer, "category", _category);
                WritePropertyIfNotNull(writer, "hostVersion", _hostVersion);
                WritePropertyIfNotNull(writer, "functionInvocationId", Utility.GetValueFromScope(scopeProps, ScopeKeys.FunctionInvocationId));
                WritePropertyIfNotNull(writer, "functionName", Utility.GetValueFromScope(scopeProps, ScopeKeys.FunctionName));
                WritePropertyIfNotNull(writer, "hostInstanceId", _hostInstanceId);
                WritePropertyIfNotNull(writer, "activityId", Utility.GetValueFromScope(scopeProps, ScriptConstants.LogPropertyActivityIdKey));
                WritePropertyIfNotNull(writer, "level", logLevel.ToString());
                WritePropertyIfNotNull(writer, nameof(exceptionDetails), exceptionDetails);
                WritePropertyIfNotNull(writer, nameof(exceptionMessage), exceptionMessage);
                WritePropertyIfNotNull(writer, nameof(exceptionType), exceptionType);
                writer.WriteEndObject();
            }

            _eventGenerator.LogAzureMonitorDiagnosticLogEvent(logLevel, _websiteHostName, AzureMonitorOperationName, AzureMonitorCategoryName, _regionName, sw.ToString());
        }

        private static void WritePropertyIfNotNull(JsonTextWriter writer, string propertyName, string propertyValue)
        {
            if (propertyValue != null)
            {
                writer.WritePropertyName(propertyName);
                writer.WriteValue(propertyValue);
            }
        }
    }
}
