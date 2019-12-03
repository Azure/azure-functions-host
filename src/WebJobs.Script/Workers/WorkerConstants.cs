// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public static class WorkerConstants
    {
        public const string HostName = "127.0.0.1";
        public const string HttpScheme = "http";

        public const int ProcessStartTimeoutSeconds = 60;
        public const int WorkerReadyCheckPollingIntervalMilliseconds = 25;
        public const int WorkerInitTimeoutSeconds = 30;
        public const string WorkerConfigFileName = "worker.config.json";
        public const string DefaultWorkersDirectoryName = "workers";

        // Section names in host.json or AppSettings
        public const string WorkersDirectorySectionName = "workersDirectory";
        public const string WorkerDirectorySectionName = "workerDirectory";

        // Worker description constants
        public const string WorkerDescriptionDefaultExecutablePath = "defaultExecutablePath";
        public const string WorkerDescriptionDefaultWorkerPath = "defaultWorkerPath";
        public const string WorkerDescription = "description";
        public const string WorkerDescriptionArguments = "arguments";

        // Profiles
        public const string WorkerDescriptionProfiles = "profiles";
        public const string WorkerDescriptionAppServiceEnvProfileName = "appServiceEnvironment";

        // Logs
        public const string LanguageWorkerConsoleLogPrefix = "LanguageWorkerConsoleLog";
        public const string FunctionConsoleLogCategoryName = "Host.Function.Console";

        // Thresholds
        public const int WorkerRestartErrorIntervalThresholdInMinutes = 30;

        // Language Worker process exit codes
        public const int SuccessExitCode = 0;
        public const int IntentionalRestartExitCode = 200;
    }
}