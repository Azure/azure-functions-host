// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Logging.ApplicationInsights
{
    internal class WebJobsTelemetryInitializer : ITelemetryInitializer
    {
        private const string ComputerNameKey = "COMPUTERNAME";
        private const string WebSiteInstanceIdKey = "WEBSITE_INSTANCE_ID";

        private static string _roleInstanceName = GetRoleInstanceName();

        public void Initialize(ITelemetry telemetry)
        {
            if (telemetry == null)
            {
                return;
            }

            telemetry.Context.Cloud.RoleInstance = _roleInstanceName;

            // Zero out all IP addresses other than Requests
            if (!(telemetry is RequestTelemetry))
            {
                telemetry.Context.Location.Ip = LoggingConstants.ZeroIpAddress;
            }

            // Apply our special scope properties
            IDictionary<string, object> scopeProps = DictionaryLoggerScope.GetMergedStateDictionary() ?? new Dictionary<string, object>();

            Guid invocationId = scopeProps.GetValueOrDefault<Guid>(ScopeKeys.FunctionInvocationId);
            if (invocationId != default(Guid))
            {
                telemetry.Context.Operation.Id = invocationId.ToString();
            }
            telemetry.Context.Operation.Name = scopeProps.GetValueOrDefault<string>(ScopeKeys.FunctionName);

            // Apply Category and LogLevel to all telemetry
            ISupportProperties telemetryProps = telemetry as ISupportProperties;
            if (telemetryProps != null)
            {
                string category = scopeProps.GetValueOrDefault<string>(LoggingKeys.CategoryName);
                if (category != null)
                {
                    telemetryProps.Properties[LoggingKeys.CategoryName] = category;
                }

                LogLevel? logLevel = scopeProps.GetValueOrDefault<LogLevel?>(LoggingKeys.LogLevel);
                if (logLevel != null)
                {
                    telemetryProps.Properties[LoggingKeys.LogLevel] = logLevel.Value.ToString();
                }
            }
        }

        private static string GetRoleInstanceName()
        {
            string instanceName = Environment.GetEnvironmentVariable(WebSiteInstanceIdKey);
            if (string.IsNullOrEmpty(instanceName))
            {
                instanceName = Environment.GetEnvironmentVariable(ComputerNameKey);
            }

            return instanceName;
        }
    }
}
