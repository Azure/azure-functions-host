// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public static class MetricEventNames
    {
        // host level events
        public const string HostStartupLatency = "host.startup.latency";

        // function level events
        public const string FunctionInvokeByTriggerFormat = "function.invoke.{0}";
        public const string FunctionCompileLatencyByLanguageFormat = "function.compile.{0}.latency";
    }
}
