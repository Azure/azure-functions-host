// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public static class RpcWorkerConstants
    {
        public const string FunctionWorkerRuntimeSettingName = "FUNCTIONS_WORKER_RUNTIME";
        // semicolon seperated string - list of runtimes to start in placeholder mode
        public const string FunctionWorkerPlaceholderModeListSettingName = "FUNCTIONS_WORKER_RUNTIME_PLACEHOLDER_MODE_LIST";
        public const string FunctionWorkerRuntimeVersionSettingName = "FUNCTIONS_WORKER_RUNTIME_VERSION";
        public const string FunctionsWorkerProcessCountSettingName = "FUNCTIONS_WORKER_PROCESS_COUNT";
        public const string FunctionsWorkerSharedMemoryDataTransferEnabledSettingName = "FUNCTIONS_WORKER_SHARED_MEMORY_DATA_TRANSFER_ENABLED";

        // Comma-separated list of directories where shared memory maps can be created for data transfer between host and worker.
        // This will override the default directories.
        public const string FunctionsUnixSharedMemoryDirectories = "FUNCTIONS_UNIX_SHARED_MEMORY_DIRECTORIES";
        public const string DotNetLanguageWorkerName = "dotnet";
        public const string DotNetIsolatedLanguageWorkerName = "dotnet-isolated";
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
        public const string WorkerDirectoryPath = "{workerDirectoryPath}";

        // Rpc message length
        public const int DefaultMaxMessageLengthBytes = int.MaxValue;

        // Capabilities
        public const string RawHttpBodyBytes = "RawHttpBodyBytes";
        public const string TypedDataCollection = "TypedDataCollection";
        public const string RpcHttpBodyOnly = "RpcHttpBodyOnly";
        public const string RpcHttpTriggerMetadataRemoved = "RpcHttpTriggerMetadataRemoved";
        public const string IgnoreEmptyValuedRpcHttpHeaders = "IgnoreEmptyValuedRpcHttpHeaders";
        public const string WorkerStatus = "WorkerStatus";
        public const string UseNullableValueDictionaryForHttp = "UseNullableValueDictionaryForHttp";
        public const string SharedMemoryDataTransfer = "SharedMemoryDataTransfer";
        public const string FunctionDataCache = "FunctionDataCache";
        public const string AcceptsListOfFunctionLoadRequests = "AcceptsListOfFunctionLoadRequests";
        public const string EnableUserCodeException = "EnableUserCodeException";
        public const string SupportsLoadResponseCollection = "SupportsLoadResponseCollection";
        public const string HandlesWorkerTerminateMessage = "HandlesWorkerTerminateMessage";
        public const string HandlesInvocationCancelMessage = "HandlesInvocationCancelMessage";
        public const string HandlesWorkerWarmupMessage = "HandlesWorkerWarmupMessage";
        public const string WorkerApplicationInsightsLoggingEnabled = nameof(WorkerApplicationInsightsLoggingEnabled);
        public const string HttpUri = "HttpUri";

        /// <summary>
        /// Indicates whether empty entries in the trigger message should be included when sending RpcInvocation data to OOP workers.
        /// </summary>
        public const string IncludeEmptyEntriesInMessagePayload = "IncludeEmptyEntriesInMessagePayload";

        // Host Capabilities
        public const string V2Compatable = "V2Compatable";
        public const string MultiStream = nameof(MultiStream);

        // dotnet executable file path components
        public const string DotNetExecutableName = "dotnet";
        public const string DotNetExecutableNameWithExtension = DotNetExecutableName + ".exe";
        public const string DotNetFolderName = "dotnet";

        // Language worker concurrency limits
        public const string FunctionsWorkerDynamicConcurrencyEnabled = "FUNCTIONS_WORKER_DYNAMIC_CONCURRENCY_ENABLED";
        public const string PythonThreadpoolThreadCount = "PYTHON_THREADPOOL_THREAD_COUNT";
        public const string PSWorkerInProcConcurrencyUpperBound = "PSWorkerInProcConcurrencyUpperBound";
        public const string DefaultConcurrencyLimit = "1000";

        // Language worker warmup
        public const string WorkerWarmupEnabled = "WORKER_WARMUP_ENABLED";
        public const string DotNetCoreDebugEngine = ".NETCore";
        public const string DotNetFrameworkDebugEngine = ".NETFramework";
        public const string DotNetFramework = "Framework";

        public const string WorkerIndexingEnabled = "WORKER_INDEXING_ENABLED";
        public const string WorkerIndexingDisabledApps = "WORKER_INDEXING_DISABLED_APPS";
    }
}