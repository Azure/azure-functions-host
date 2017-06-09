// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public static class MetricEventNames
    {
        // host level events
        public const string ApplicationStartLatency = "host.application.start";
        public const string HostStartupLatency = "host.startup.latency";
        public const string ApplicationInsightsEnabled = "host.applicationinsights.enabled";
        public const string ApplicationInsightsDisabled = "host.applicationinsights.disabled";

        // function level events
        public const string FunctionInvokeLatency = "function.invoke.latency";
        public const string FunctionBindingTypeFormat = "function.binding.{0}";
        public const string FunctionBindingTypeDirectionFormat = "function.binding.{0}.{1}";
        public const string FunctionCompileLatencyByLanguageFormat = "function.compile.{0}.latency";
        public const string FunctionInvokeThrottled = "function.invoke.throttled";
    }
}
