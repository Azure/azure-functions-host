// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ScriptConstants
    {
        public const string AzureFunctionsWebHookContextKey = "MS_AzureFunctionsWebHookContext";

        public const string AzureFunctionsHttpResponseKey = "MS_AzureFunctionsHttpResponse";

        public const string DefaultSystemTriggerParameterName = "triggerValue";

        public const string HostMetadataFileName = "host.json";

        internal const string FunctionMetadataFileName = "function.json";

        internal const string ApiMetadataFileName = "api.yaml";
    }
}
