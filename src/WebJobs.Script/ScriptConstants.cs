// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ScriptConstants
    {
        public const string AzureFunctionsWebHookContextKey = "MS_AzureFunctionsWebHookContext";
        public const string AzureFunctionsWebHookDataKey = "MS_AzureFunctionsWebHookData";
        public const string AzureFunctionsHttpResponseKey = "MS_AzureFunctionsHttpResponse";
        public const string AzureFunctionsHttpRouteDataKey = "MS_AzureFunctionsHttpRouteData";
        public const string AzureFunctionsHttpRequestAuthorizationLevelKey = "MS_AzureFunctionsAuthorizationLevel";
        public const string AzureFunctionsHttpFunctionKey = "MS_AzureFunctionsHttpFunction";
        public const string AzureFunctionsRequestIdKey = "MS_AzureFunctionsRequestID";

        public const string TracePropertyPrimaryHostKey = "MS_PrimaryHost";
        public const string TracePropertyFunctionNameKey = "MS_FunctionName";
        public const string TracePropertyEventNameKey = "MS_EventName";
        public const string TracePropertyEventDetailsKey = "MS_EventDetails";
        public const string TracePropertyIsUserTraceKey = "MS_IsUserTrace";
        public const string TracePropertyIsSystemTraceKey = "MS_IsSystemTrace";

        public const string TraceSourceSecretManagement = "SecretManagement";
        public const string TraceSourceHostAdmin = "HostAdmin";
        public const string TraceSourceFileWatcher = "FileWatcher";
        public const string TraceSourceSwagger = "SwaggerDocumentation";
        public const string TraceSourceHttpHandler = "HttpRequestTraceHandler";

        // Define all system parameters we inject with a prefix to avoid collisions
        // with user parameters
        public const string SystemTriggerParameterName = "_triggerValue";
        public const string SystemExecutionContextParameterName = "_context";
        public const string SystemLogParameterName = "_log";
        public const string SystemBinderParameterName = "_binder";
        public const string SystemReturnParameterBindingName = "$return";
        public const string SystemReturnParameterName = "_return";

        public const string DebugSentinelFileName = "debug_sentinel";
        public const string HostMetadataFileName = "host.json";
        public const string FunctionMetadataFileName = "function.json";
        public const string DefaultHttpRoutePrefix = "api";
        public const string DefaultMasterKeyName = "master";
        public const string DefaultFunctionKeyName = "default";

        public const string AntaresLogIdHeaderName = "X-ARR-LOG-ID";
        public const string CheckLoadQueryParameterName = "checkLoad";
        public const string DynamicSku = "Dynamic";
        public const string DefaultProductionSlotName = "production";

        public const string FeatureFlagDisableShadowCopy = "DisableShadowCopy";

        public const string AdminJwtValidAudienceFormat = "https://{0}.azurewebsites.net/azurefunctions";
        public const string AdminJwtValidIssuerFormat = "https://{0}.scm.azurewebsites.net";

        public const string SwaggerFileName = "swagger.json";
        public const string AzureFunctionsSystemDirectoryName = ".azurefunctions";
        public const string SwaggerDirectoryName = "swagger";
        public const string DocumentationResourceName = ".azurefunctions.swagger";
        public static readonly ImmutableArray<string> HttpMethods = ImmutableArray.Create("get", "post", "delete", "head", "patch", "put", "options");
        public const string HttpMethodConstraintName = "httpMethod";
        public static readonly ImmutableArray<string> AssemblyFileTypes = ImmutableArray.Create(".dll", ".exe");
        public const string SwaggerDocumentationKey = "swaggerdocumentationkey";
        public const int MaximumHostIdLength = 32;
        public const string AppInsightsInstrumentationKey = "APPINSIGHTS_INSTRUMENTATIONKEY";
    }
}
