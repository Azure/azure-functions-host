// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    public class HostMetrics : IHostMetrics
    {
        private readonly IEnvironment _environment;

        public const string MeterName = "Microsoft.Azure.WebJobs.Script.Host.Internal";
        public const string CloudPlatformName = "azure_functions";
        public const string AppFailureCount = "azure.functions.app_failures";
        public const string ActiveInvocationCount = "azure.functions.active_invocations";
        public const string StartedInvocationCount = "azure.functions.started_invocations";

        private Counter<long> _appFailureCount;
        private Counter<long> _startedInvocationCount;

        private KeyValuePair<string, object>? _cachedFunctionGroupTag = null;

        public HostMetrics(IMeterFactory meterFactory, IEnvironment environment)
        {
            if (meterFactory == null)
            {
                throw new ArgumentNullException(nameof(meterFactory));
            }

            _environment = environment ?? throw new ArgumentNullException(nameof(environment));

            var instanceId = environment.GetInstanceId();
            var cloudName = environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.CloudName, string.Empty);
            var region = environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.RegionName, string.Empty);
            var armResourceId = environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.WebsiteArmResourceId, string.Empty);
            var appName = environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.AzureWebsiteName, string.Empty);

            var hostMeterOptions = new MeterOptions(MeterName)
            {
                Version = "1.0.0",
                Tags = new TagList()
                {
                    { TelemetryAttributes.CloudProvider, cloudName },
                    { TelemetryAttributes.CloudPlatform, CloudPlatformName },
                    { TelemetryAttributes.CloudRegion, region },
                    { TelemetryAttributes.CloudResourceId, armResourceId },
                    { TelemetryAttributes.ServiceInstanceId, instanceId },
                    { TelemetryAttributes.ServiceName, appName }
                }
            };
            var meter = meterFactory.Create(hostMeterOptions);

            _appFailureCount = meter.CreateCounter<long>(AppFailureCount, "numeric", "Number of times the host has failed to start.");
            _startedInvocationCount = meter.CreateCounter<long>(StartedInvocationCount, "numeric", "Number of function invocations that have started.");
        }

        private KeyValuePair<string, object> FunctionGroupTag
        {
            get
            {
                if (_cachedFunctionGroupTag != null)
                {
                    return _cachedFunctionGroupTag.Value;
                }

                var functionGroup = _environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.FunctionsTargetGroup, string.Empty);
                var functionGroupTag = new KeyValuePair<string, object>(TelemetryAttributes.AzureFunctionsGroup, functionGroup);

                if (!string.IsNullOrEmpty(functionGroup))
                {
                    _cachedFunctionGroupTag = functionGroupTag;
                }

                return functionGroupTag;
            }

            set
            {
                _cachedFunctionGroupTag = value;
            }
        }

        public void AppFailure() => _appFailureCount.Add(1, FunctionGroupTag);

        public void IncrementStartedInvocationCount() => _startedInvocationCount.Add(1, FunctionGroupTag);
    }
}
