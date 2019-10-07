// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public static class LanguageWorkerConstants
    {
        public const string FunctionWorkerRuntimeSettingName = "FUNCTIONS_WORKER_RUNTIME";
        public const string FunctionWorkerRuntimeVersionSettingName = "FUNCTIONS_WORKER_RUNTIME_VERSION";
        public const string FunctionsWorkerProcessCountSettingName = "FUNCTIONS_WORKER_PROCESS_COUNT";
        public const string DotNetLanguageWorkerName = "dotnet";
        public const string NodeLanguageWorkerName = "node";
        public const string JavaLanguageWorkerName = "java";
        public const string PowerShellLanguageWorkerName = "powershell";
        public const string PythonLanguageWorkerName = "python";
        public const string WorkerConfigFileName = "worker.config.json";
        public const string DefaultWorkersDirectoryName = "workers";

        // Section names in host.json or AppSettings
        public const string LanguageWorkersSectionName = "languageWorkers";

        // Worker description constants
        public const string WorkerDescriptionLanguage = "language";
        public const string OSPlaceholder = "{os}";
        public const string ArchitecturePlaceholder = "{architecture}";
        public const string RuntimeVersionPlaceholder = "%" + FunctionWorkerRuntimeVersionSettingName + "%";

        // Rpc message length
        public const int DefaultMaxMessageLengthBytes = 128 * 1024 * 1024;

        // Rpc Http Constants
        public const string RpcHttpBody = "body";
        public const string RpcHttpHeaders = "headers";
        public const string RpcHttpEnableContentNegotiation = "enableContentNegotiation";
        public const string RpcHttpCookies = "cookies";
        public const string RpcHttpStatusCode = "statusCode";
        public const string RpcHttpStatus = "status";

        // Capabilites
        public const string RawHttpBodyBytes = "RawHttpBodyBytes";
        public const string TypedDataCollection = "TypedDataCollection";
        public const string RpcHttpBodyOnly = "RpcHttpBodyOnly";
        public const string RpcHttpTriggerMetadataRemoved = "RpcHttpTriggerMetadataRemoved";
    }
}