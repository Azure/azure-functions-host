// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticTraceWriter : TraceWriter
    {
        internal const string AzureMonitorCategoryName = "FunctionExecutionEvent";
        internal const string AzureMonitorOperationName = "Microsoft.Web/sites/functions/execution";

        private readonly string _hostVersion = ScriptHost.Version;
        private readonly string _regionName;
        private readonly JsonSerializerSettings _serializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly IEventGenerator _eventGenerator;
        private readonly ScriptSettingsManager _settingsManager;

        public DiagnosticTraceWriter(IEventGenerator eventGenerator, ScriptSettingsManager settingsManager, TraceLevel level)
            : base(level)
        {
            _eventGenerator = eventGenerator;
            _settingsManager = settingsManager;
            _regionName = _settingsManager.GetSetting(EnvironmentSettingNames.RegionName) ?? string.Empty;
        }

        public override void Trace(TraceEvent traceEvent)
        {
            if (Level < traceEvent.Level)
            {
                return;
            }

            // We cannot cache all of these values as they can change on-the-fly (for example, during a slot swap)            
            string subscriptionId = Utility.GetSubscriptionId();
            string resourceGroup = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteResourceGroup);
            string websiteName = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteName);
            string slotName = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteSlotName);

            string resourceId = GenerateResourceId(subscriptionId, resourceGroup, websiteName, slotName);

            (var exceptionType, var exceptionMessage, var exceptionDetails) = traceEvent.GetExceptionDetails();

            // Build up a JSON string for the Azure Monitor 'properties' bag
            // Any null values will be dropped from the resulting JSON string
            var properties = new
            {
                exceptionType,
                exceptionMessage,
                exceptionDetails,
                message = Sanitizer.Sanitize(traceEvent.Message),
                category = traceEvent.Source,
                hostVersion = _hostVersion,
                functionInvocationId = traceEvent.GetPropertyValueOrNull(ScriptConstants.TracePropertyFunctionInvocationIdKey),
                functionName = traceEvent.GetPropertyValueOrNull(ScriptConstants.TracePropertyFunctionNameKey),
                hostInstanceId = traceEvent.GetPropertyValueOrNull(ScriptConstants.TracePropertyInstanceIdKey),
                activityId = traceEvent.GetPropertyValueOrNull(ScriptConstants.TracePropertyActivityIdKey),
                level = traceEvent.Level.ToString()
            };

            string jsonString = JsonConvert.SerializeObject(properties, Formatting.None, _serializerSettings);

            _eventGenerator.LogFunctionDiagnosticEvent(traceEvent.Level, resourceId, AzureMonitorOperationName, AzureMonitorCategoryName, _regionName, jsonString);
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