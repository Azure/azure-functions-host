// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    public class JavaLanguageWorkerConfig : WorkerConfig
    {
        public JavaLanguageWorkerConfig()
            : base()
        {
            Extension = ".jar";
            Language = "Java";
            var javaHome = ScriptSettingsManager.Instance.Configuration.GetSection("JAVA_HOME").Value ?? string.Empty;
            if (ScriptSettingsManager.Instance.IsAzureEnvironment)
            {
                // on azure, force latest jdk
                javaHome = Path.Combine(javaHome, "..", "jdk1.8.0_111");
            }
            var javaPath = Path.Combine(javaHome, "bin", "java");
            ExecutablePath = Path.GetFullPath(javaPath);

            var settingsManager = ScriptSettingsManager.Instance;
            var javaSection = settingsManager.Configuration
                .GetSection("workers")
                .GetSection(Language);

            WorkerPath = javaSection.GetSection("path").Value ?? settingsManager.GetSetting("AzureWebJobsJavaWorkerPath");
            if (string.IsNullOrEmpty(WorkerPath))
            {
                WorkerPath = Path.Combine(Location, "workers", "java", "azure-functions-java-worker.jar");
            }
            ExecutableArguments.Add("-jar");

            var javaOpts = settingsManager.GetSetting("JAVA_OPTS");
            if (!string.IsNullOrEmpty(javaOpts))
            {
                ExecutableArguments.Add(javaOpts);
            }

            var debugPortConfig = javaSection.GetSection("debug").Value;
            if (debugPortConfig != null)
            {
                int port = 5005;
                try
                {
                    port = Convert.ToInt32(debugPortConfig);
                }
                catch
                {
                }
                ExecutableArguments.Add($"-agentlib:jdwp=transport=dt_socket,server=y,suspend=n,address={port}");
            }
        }
    }
}
