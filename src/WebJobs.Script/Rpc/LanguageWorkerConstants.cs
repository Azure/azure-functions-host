// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public static class LanguageWorkerConstants
    {
        public const string FunctionWorkerRuntimeSettingName = "FUNCTIONS_WORKER_RUNTIME";
        public const string FunctionsWorkerProcessCountSettingName = "FUNCTIONS_WORKER_PROCESS_COUNT";
        public const string DotNetLanguageWorkerName = "dotnet";
        public const string NodeLanguageWorkerName = "node";
        public const string JavaLanguageWorkerName = "java";
        public const string PowerShellLanguageWorkerName = "powershell";
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

        //Logs
        public const string LanguageWorkerConsoleLogPrefix = "LanguageWorkerConsoleLog";
        public const string FunctionConsoleLogCategoryName = "Host.Function.Console";
    }
}
