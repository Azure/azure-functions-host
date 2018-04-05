// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticLogger : ILogger
    {
        internal const string AzureMonitorCategoryName = "FunctionExecutionEvent";
        internal const string AzureMonitorOperationName = "Microsoft.Web/sites/functions/execution";

        private readonly string _hostVersion = ScriptHost.Version;
        private readonly string _regionName;
        private readonly JsonSerializerSettings _serializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly string _category;
        private readonly IEventGenerator _eventGenerator;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly Func<string, LogLevel, bool> _filter;

        public DiagnosticLogger(string category, IEventGenerator eventGenerator, ScriptSettingsManager settingsManager, Func<string, LogLevel, bool> filter)
        {
            _category = category;
            _eventGenerator = eventGenerator;
            _settingsManager = settingsManager;
            _filter = filter;
            _regionName = _settingsManager.GetSetting(EnvironmentSettingNames.RegionName) ?? string.Empty;
        }

        public IDisposable BeginScope<TState>(TState state) => DictionaryLoggerScope<DiagnosticLogger>.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            if (_filter == null)
            {
                return true;
            }

            return _filter(_category, logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string formattedMessage = formatter?.Invoke(state, exception);

            // We cannot cache all of these values as they can change on-the-fly (for example, during a slot swap)
            string subscriptionId = Utility.GetSubscriptionId();
            string resourceGroup = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteResourceGroup);
            string websiteName = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteName);
            string slotName = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteSlotName);

            string resourceId = GenerateResourceId(subscriptionId, resourceGroup, websiteName, slotName);

            (string exceptionType, string exceptionMessage, string exceptionDetails) = exception.GetExceptionDetails();

            var scopeProps = DictionaryLoggerScope<DiagnosticLogger>.GetCurrentScopeValues();

            // Build up a JSON string for the Azure Monitor 'properties' bag
            // Any null values will be dropped from the resulting JSON string
            var properties = new
            {
                exceptionType,
                exceptionMessage,
                exceptionDetails,
                message = Sanitizer.Sanitize(formattedMessage),
                category = _category,
                hostVersion = _hostVersion,
                functionInvocationId = Utility.GetValueFromScope(scopeProps, ScopeKeys.FunctionInvocationId),
                functionName = Utility.GetValueFromScope(scopeProps, ScopeKeys.FunctionName),
                hostInstanceId = Utility.GetValueFromScope(scopeProps, ScopeKeys.HostInstanceId),
                activityId = Utility.GetValueFromScope(scopeProps, ScriptConstants.LogPropertyActivityIdKey),
                level = logLevel.ToString()
            };

            string jsonString = JsonConvert.SerializeObject(properties, Formatting.None, _serializerSettings);

            _eventGenerator.LogFunctionDiagnosticEvent(logLevel, resourceId, AzureMonitorOperationName, AzureMonitorCategoryName, _regionName, jsonString);
        }

        internal static string GenerateResourceId(string subscriptionId, string resourceGroup, string websiteName, string slotName)
        {
            string resourceId = $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/sites/{websiteName}";

            if (!string.IsNullOrEmpty(slotName) &&
                !string.Equals(slotName, ScriptConstants.DefaultProductionSlotName, StringComparison.OrdinalIgnoreCase))
            {
                resourceId += $"/slots/{slotName}";
            }

            return resourceId;
        }
    }
}
