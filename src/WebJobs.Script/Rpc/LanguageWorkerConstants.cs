// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public static class LanguageWorkerConstants
    {
        public const string FunctionWorkerRuntimeSettingName = "FUNCTIONS_WORKER_RUNTIME";
        public const string DotNetLanguageWorkerName = "dotnet";
        public const string NodeLanguageWorkerName = "node";
        public const string JavaLanguageWrokerName = "java";
        public const string DefaultWorkersDirectoryName = "workers";
        public const string WorkersDirectorySectionName = "workersDirectory";
        public const string WorkerConfigFileName = "worker.config.json";
        public const string LanguageWorkerSectionName = "languageWorker";
        public const string WorkerDescriptionLanguage = "language";
        public const string WorkerDescriptionExtension = "extension";
        public const string WorkerDescriptionDefaultExecutablePath = "defaultExecutablePath";
        public const string WorkerDescriptionDefaultWorkerPath = "defaultWorkerPath";
        public const string WorkerDescription = "Description";
        public const string WorkerDescriptionArguments = "Arguments";
    }
}
