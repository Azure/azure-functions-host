﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ScriptConstants
    {
        public const string AzureFunctionsWebHookContextKey = "MS_AzureFunctionsWebHookContext";
        public const string AzureFunctionsHttpResponseKey = "MS_AzureFunctionsHttpResponse";
        public const string AzureFunctionsHttpRequestAuthorizationLevelKey = "MS_AzureFunctionsAuthorizationLevel";
        public const string AzureFunctionsHttpRequestKeyNameKey = "MS_AzureFunctionsKeyId";
        public const string AzureFunctionsHttpRequestAuthorizationDisabledKey = "MS_AzureFunctionsAuthorizationDisabled";
        public const string AzureFunctionsHttpFunctionKey = "MS_AzureFunctionsHttpFunction";
        public const string AzureFunctionsRequestIdKey = "MS_AzureFunctionsRequestID";
        public const string AzureFunctionsColdStartKey = "MS_AzureFunctionsColdStart";
        public const string AzureFunctionsHttpRequestKey = "MS_AzureFunctionsHttpRequest";
        public const string AzureProxyFunctionExecutorKey = "MS_AzureProxyFunctionExecutor";
        public const string AzureFunctionsHostKey = "MS_AzureFunctionsHost";
        public const string AzureFunctionsNestedProxyCount = "MS_AzureFunctionsNestedProxyCount";
        public const string AzureFunctionsProxyResult = "MS_AzureFunctionsProxyResult";

        public const string LogPropertyPrimaryHostKey = "MS_PrimaryHost";
        public const string LogPropertySourceKey = "MS_Source";
        public const string LogPropertyFunctionNameKey = "MS_FunctionName";
        public const string LogPropertyEventNameKey = "MS_EventName";
        public const string LogPropertyEventDetailsKey = "MS_EventDetails";
        public const string LogPropertyIsUserLogKey = "MS_IsUserLog";
        public const string LogPropertyIsSystemLogKey = "MS_IsSystemLog";
        public const string LogPropertyFunctionInvocationIdKey = "MS_FunctionInvocationId";
        public const string LogPropertyHostInstanceIdKey = "HostInstanceId";
        public const string LogPropertyActivityIdKey = "MS_ActivityId";

        public const string TraceSourceSecretManagement = "SecretManagement";
        public const string TraceSourceHostAdmin = "HostAdmin";
        public const string TraceSourceFileWatcher = "FileWatcher";
        public const string TraceSourceHttpHandler = "HttpRequestTraceHandler";

        public const string LoggerHttpRequest = "MS_HttpRequest";

        public const string LogCategoryHostController = "Host.Controllers.Host";
        public const string LogCategoryFunctionsController = "Host.Controllers.Functions";
        public const string LogCategoryInstanceController = "Host.Controllers.Instance";
        public const string LogCategoryKeysController = "Host.Controllers.Keys";
        public const string LogCategoryHostGeneral = "Host.General";
        public const string LogCategoryHostMetrics = "Host.Metrics";
        public const string LogCategoryHost = "Host";
        public const string LogCategoryFunction = "Function";
        public const string LogCategoryWorker = "Worker";
        public const string LogCategoryMigration = "Host.Migration";
        public const string ConsoleLoggingMode = "consoleLoggingMode";

        // Define all system parameters we inject with a prefix to avoid collisions
        // with user parameters
        public const string SystemTriggerParameterName = "_triggerValue";
        public const string SystemExecutionContextParameterName = "_context";
        public const string SystemLogParameterName = "_log";
        public const string SystemBinderParameterName = "_binder";
        public const string SystemReturnParameterBindingName = "$return";
        public const string SystemReturnParameterName = "_return";
        public const string SystemLoggerParameterName = "_logger";

        public const string DebugSentinelFileName = "debug_sentinel";
        public const string HostMetadataFileName = "host.json";
        public const string FunctionMetadataFileName = "function.json";
        public const string ProxyMetadataFileName = "proxies.json";
        public const string ExtensionsMetadataFileName = "extensions.json";

        public const string DefaultMasterKeyName = "master";
        public const string DefaultFunctionKeyName = "default";
        public const string ColdStartEventName = "ColdStart";

        public const string AntaresLogIdHeaderName = "X-ARR-LOG-ID";
        public const string AntaresScaleOutHeaderName = "X-FUNCTION-SCALEOUT";
        public const string AntaresColdStartHeaderName = "X-MS-COLDSTART";
        public const string DynamicSku = "Dynamic";
        public const string DefaultProductionSlotName = "production";

        public const string AzureProxyFunctionLocalRedirectKey = "MS_ProxyLocalRedirectCount";
        public const int AzureProxyFunctionMaxLocalRedirects = 10;

        public const string FeatureFlagDisableShadowCopy = "DisableShadowCopy";
        public const string FeatureFlagsEnableDynamicExtensionLoading = "EnableDynamicExtensionLoading";

        public const string AdminJwtValidAudienceFormat = "https://{0}.azurewebsites.net/azurefunctions";
        public const string AdminJwtValidIssuerFormat = "https://{0}.scm.azurewebsites.net";

        public const string AzureFunctionsSystemDirectoryName = ".azurefunctions";
        public const string HttpMethodConstraintName = "httpMethod";
        public const string Snapshot = "snapshot";
        public const string Runtime = "runtime";
        public const string NugetFallbackFolderRootName = "FuncNuGetFallback";
        public const string NugetXmlDocModeSettingName = "NUGET_XMLDOC_MODE";
        public const string NugetXmlDocSkipMode = "skip";

        public const int MaximumHostIdLength = 32;
        public const int DynamicSkuConnectionLimit = 50;

        public const string ExtensionsProjectFileName = "extensions.csproj";
        public const string ExtensionsPackageId = "Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator";
        public const string PackageReferenceElementName = "PackageReference";
        public const string PackageReferenceVersionElementName = "Version";
        public const int HostTimeoutSeconds = 30;
        public const int HostPollingIntervalMilliseconds = 25;
        public const int MaximumSecretBackupCount = 10;

        public const string LinuxLogEventStreamName = "MS_FUNCTION_LOGS";
        public const string LinuxMetricEventStreamName = "MS_FUNCTION_METRICS";
        public const string LinuxFunctionDetailsEventStreamName = "MS_FUNCTION_DETAILS";

        public const string DurableTaskPropertyName = "durableTask";
        public const string DurableTaskHubName = "HubName";

        public static readonly ImmutableArray<string> HttpMethods = ImmutableArray.Create("get", "post", "delete", "head", "patch", "put", "options");
        public static readonly ImmutableArray<string> AssemblyFileTypes = ImmutableArray.Create(".dll", ".exe");
    }
}
