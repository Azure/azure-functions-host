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
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME") ?? string.Empty;
            if (ScriptSettingsManager.Instance.IsAzureEnvironment)
            {
                // on azure, force latest jdk
                javaHome = Path.Combine(javaHome, "..", "jdk1.8.0_111");
            }
            var javaPath = Path.Combine(javaHome, "bin", "java");
            ExecutablePath = Path.GetFullPath(javaPath);
            var workerJar = Environment.GetEnvironmentVariable("AzureWebJobsJavaWorkerPath");
            if (string.IsNullOrEmpty(workerJar))
            {
                workerJar = Path.Combine(Location, "workers", "java", "azure-functions-java-worker.jar");
            }

            // Load the JVM starting parameters to support attach to debugging.
            var javaOpts = Environment.GetEnvironmentVariable("JAVA_OPTS") ?? string.Empty;
            WorkerPath = $"-jar {javaOpts} \"{workerJar}\"";
            Extension = ".jar";
            Language = "Java";
        }
    }
}
