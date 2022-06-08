// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public static class WorkerConstants
    {
        public const string HostName = "127.0.0.1";
        public const string HttpScheme = "http";

        public const int WorkerReadyCheckPollingIntervalMilliseconds = 25;
        public const string WorkerConfigFileName = "worker.config.json";
        public const string DefaultWorkersDirectoryName = "workers";

        // Section names in host.json or AppSettings
        public const string WorkersDirectorySectionName = "workersDirectory";
        public const string WorkerDirectorySectionName = "workerDirectory";

        // Environment variables names
        public const string FunctionsWorkerDirectorySettingName = "FUNCTIONS_WORKER_DIRECTORY";

        // Worker description constants
        public const string WorkerDescriptionDefaultExecutablePath = "defaultExecutablePath";
        public const string WorkerDescriptionDefaultWorkerPath = "defaultWorkerPath";
        public const string WorkerDescription = "description";
        public const string ProcessCount = "processOptions";
        public const string WorkerDescriptionArguments = "arguments";
        public const string WorkerDescriptionDefaultRuntimeVersion = "defaultRuntimeVersion";

        // Profiles
        public const string WorkerDescriptionProfiles = "profiles";
        public const string WorkerDescriptionProfileName = "profileName";
        public const string WorkerDescriptionProfileConditions = "conditions";
        public const string WorkerDescriptionProfileConditionType = "conditionType";
        public const string WorkerDescriptionProfileEnvironmentCondition = "environment";
        public const string WorkerDescriptionProfileHostPropertyCondition = "hostProperty";
        public const string WorkerDescriptionProfileConditionName = "conditionName";
        public const string WorkerDescriptionProfileConditionExpression = "conditionExpression";
        public const string WorkerDescriptionAppServiceEnvProfileName = "appServiceEnvironment";

        // Logs
        public const string LanguageWorkerConsoleLogPrefix = "LanguageWorkerConsoleLog";
        public const string FunctionConsoleLogCategoryName = "Host.Function.Console";

        // Thresholds
        public const int WorkerRestartErrorIntervalThresholdInMinutes = 30;

        // Language Worker process exit codes
        public const int SuccessExitCode = 0;
        public const int IntentionalRestartExitCode = 200;

        // Http Constants
        public const string HttpBody = "body";
        public const string HttpHeaders = "headers";
        public const string HttpEnableContentNegotiation = "enableContentNegotiation";
        public const string HttpCookies = "cookies";
        public const string HttpStatusCode = "statusCode";
        public const string HttpStatus = "status";
    }
}