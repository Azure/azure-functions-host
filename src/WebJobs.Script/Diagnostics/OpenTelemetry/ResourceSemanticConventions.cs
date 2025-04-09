// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal static class ResourceSemanticConventions
    {
        // Service
        internal const string ServiceName = "service.name";
        internal const string ServiceVersion = "service.version";
        internal const string ServiceInstanceId = "service.instance.id";

        // Cloud
        internal const string CloudProvider = "cloud.provider";
        internal const string CloudPlatform = "cloud.platform";
        internal const string CloudRegion = "cloud.region";
        internal const string CloudResourceId = "cloud.resource_id";

        //FaaS
        internal const string FaaSTrigger = "faas.trigger";
        internal const string FaaSInvocationId = "faas.invocation_id";
        internal const string FaaSColdStart = "faas.coldstart";
        internal const string FaaSName = "faas.name";
        internal const string FaaSInstance = "faas.instance";

        // Process
        internal const string ProcessId = "process.pid";

        // Http
        internal const string QueryUrl = "url.query";
        internal const string FullUrl = "url.full";
        internal const string HttpRoute = "http.route";

        // AI
        internal const string AISDKPrefix = "ai.sdk.prefix";
    }
}
