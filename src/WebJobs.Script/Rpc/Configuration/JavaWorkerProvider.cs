﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class JavaWorkerProvider : IWorkerProvider
    {
        private readonly string _pathToWorkerDir;

        public JavaWorkerProvider(string workerDir)
        {
           _pathToWorkerDir = Path.Combine(workerDir, LanguageWorkerConstants.JavaLanguageWorkerName);
        }

        public WorkerDescription GetDescription() => new WorkerDescription
        {
            Language = LanguageWorkerConstants.JavaLanguageWorkerName,
            Extension = ".jar",
            DefaultWorkerPath = "azure-functions-java-worker.jar",
            WorkerDirectory = _pathToWorkerDir
        };

        public bool TryConfigureArguments(WorkerProcessArguments args, IConfiguration config, ILogger logger)
        {
            var options = new DefaultWorkerOptions();
            var javaWorkerSection = $"{LanguageWorkerConstants.LanguageWorkersSectionName}:{LanguageWorkerConstants.JavaLanguageWorkerName}";
            config.GetSection(javaWorkerSection).Bind(options);
            var env = new JavaEnvironment();
            config.Bind(env);
            if (string.IsNullOrEmpty(env.JAVA_HOME))
            {
                logger.LogTrace("Unable to configure java worker. Could not find JAVA_HOME app setting.");
                return false;
            }

            args.ExecutablePath = Path.GetFullPath(Path.Combine(env.ResolveJavaHome(), "bin", "java"));
            args.ExecutableArguments.Add("-jar");

            if (!string.IsNullOrWhiteSpace(options.Debug))
            {
                if (!env.HasJavaOpts)
                {
                    var debugOpts = $"-agentlib:jdwp=transport=dt_socket,server=y,suspend=n,address={options.Debug}";
                    args.ExecutableArguments.Add(debugOpts);
                }
                else
                {
                    logger.LogWarning("Both JAVA_OPTS and debug address settings found. Defaulting to JAVA_OPTS.");
                }
            }

            if (env.HasJavaOpts)
            {
                args.ExecutableArguments.Add(env.JAVA_OPTS);
            }
            return true;
        }

        public string GetWorkerDirectoryPath()
        {
            return _pathToWorkerDir;
        }

        private class JavaEnvironment
        {
            public string JAVA_HOME { get; set; } = string.Empty;

            public string JAVA_OPTS { get; set; } = string.Empty;

            public string WEBSITE_INSTANCE_ID { get; set; } = string.Empty;

            public bool IsAzureEnvironment => !string.IsNullOrEmpty(WEBSITE_INSTANCE_ID);

            public bool HasJavaOpts => !string.IsNullOrEmpty(JAVA_OPTS);

            public string ResolveJavaHome()
            {
                if (IsAzureEnvironment)
                {
                    return Path.Combine(JAVA_HOME, "..", "zulu8.23.0.3-jdk8.0.144-win_x64");
                }
                else
                {
                    return JAVA_HOME;
                }
            }
        }
    }
}
