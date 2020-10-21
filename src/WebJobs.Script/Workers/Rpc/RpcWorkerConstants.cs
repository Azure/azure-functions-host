﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public static class RpcWorkerConstants
    {
        public const string FunctionWorkerRuntimeSettingName = "FUNCTIONS_WORKER_RUNTIME";
        public const string FunctionWorkerRuntimeVersionSettingName = "FUNCTIONS_WORKER_RUNTIME_VERSION";
        public const string FunctionsWorkerProcessCountSettingName = "FUNCTIONS_WORKER_PROCESS_COUNT";
        public const string FunctionsWorkerSharedMemoryDataTransferSettingName = "FUNCTIONS_WORKER_SHARED_MEMORY_DATA_TRANSFER";
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
        public const int DefaultMaxMessageLengthBytes = int.MaxValue;

        // Capabilites
        public const string RawHttpBodyBytes = "RawHttpBodyBytes";
        public const string TypedDataCollection = "TypedDataCollection";
        public const string RpcHttpBodyOnly = "RpcHttpBodyOnly";
        public const string RpcHttpTriggerMetadataRemoved = "RpcHttpTriggerMetadataRemoved";
        public const string IgnoreEmptyValuedRpcHttpHeaders = "IgnoreEmptyValuedRpcHttpHeaders";
        public const string WorkerStatus = "WorkerStatus";
        public const string UseNullableValueDictionaryForHttp = "UseNullableValueDictionaryForHttp";
        public const string SharedMemoryDataTransfer = "SharedMemoryDataTransfer";

        // Host Capabilites
        public const string V2Compatable = "V2Compatable";

        // dotnet executable file path components
        public const string DotNetExecutableName = "dotnet";
        public const string DotNetExecutableNameWithExtension = DotNetExecutableName + ".exe";
        public const string DotNetFolderName = "dotnet";
    }
}