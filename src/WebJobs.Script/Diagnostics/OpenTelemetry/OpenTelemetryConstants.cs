// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal static class OpenTelemetryConstants
    {
        internal const string AzureCloudProviderValue = "azure";
        internal const string AzurePlatformValue = "azure_functions";
        internal const string SDKPrefix = "azurefunctions";
        internal const string AzureFunctionsGroup = "azure.functions.group";
        internal const string HttpTriggerType = "http";
        internal const string WebJobsActivitySourceVersion = "1.0.0";
        internal const string HostActivitySourceVersion = "1.0.0";
        internal const string SpecializationOperationName = "init";

        internal static class ActivitySourceNames
        {
            internal const string WebJobs = "Microsoft.Azure.WebJobs";
            internal const string Host = "Microsoft.Azure.Functions.Host";
            internal const string ServiceBusProcessor = "Azure.Messaging.ServiceBus.ServiceBusProcessor";
            internal const string EventHubsProcessor = "Azure.Messaging.EventHubs.EventProcessor";
            internal const string Mcp = "Azure.Functions.Extensions.Mcp";
            internal const string WebJobsExtensions = "Microsoft.Azure.WebJobs.Extensions.*";
            internal const string DurableTask = "WebJobs.Extensions.DurableTask";
            internal const string DurableTaskWildcard = "DurableTask.*";
        }
    }
}
