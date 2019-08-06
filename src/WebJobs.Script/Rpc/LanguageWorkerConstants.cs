// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public static class LanguageWorkerConstants
    {
        public const int ProcessStartTimeoutSeconds = 60;
        public const string FunctionWorkerRuntimeSettingName = "FUNCTIONS_WORKER_RUNTIME";
        public const string FunctionsWorkerProcessCountSettingName = "FUNCTIONS_WORKER_PROCESS_COUNT";
        public const string DotNetLanguageWorkerName = "dotnet";
        public const string NodeLanguageWorkerName = "node";
        public const string JavaLanguageWorkerName = "java";
        public const string PowerShellLanguageWorkerName = "powershell";
        public const string PythonLanguageWorkerName = "python";
        public const string WorkerConfigFileName = "worker.config.json";
        public const string DefaultWorkersDirectoryName = "workers";

        // Section names in host.json
        public const string LanguageWorkersSectionName = "languageWorkers";
        public const string WorkersDirectorySectionName = "workersDirectory";
        public const string WorkerDirectorySectionName = "workerDirectory";

        // Worker description constants
        public const string WorkerDescriptionLanguage = "language";
        public const string WorkerDescriptionDefaultExecutablePath = "defaultExecutablePath";
        public const string WorkerDescriptionDefaultWorkerPath = "defaultWorkerPath";
        public const string WorkerDescription = "description";
        public const string WorkerDescriptionArguments = "arguments";

        // Profiles
        public const string WorkerDescriptionProfiles = "profiles";
        public const string WorkerDescriptionAppServiceEnvProfileName = "appServiceEnvironment";

        public const int DefaultMaxMessageLengthBytes = 128 * 1024 * 1024;

        // Logs
        public const string LanguageWorkerConsoleLogPrefix = "LanguageWorkerConsoleLog";
        public const string FunctionConsoleLogCategoryName = "Host.Function.Console";

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

        // Thresholds
        public const int WorkerRestartErrorIntervalThresholdInMinutes = 30;

        // Language Worker process exit codes
        public const int SuccessExitCode = 0;
        public const int IntentionalRestartExitCode = 200;
    }
}