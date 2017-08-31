// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    public class JavaLanguageWorkerConfig : WorkerConfig
    {
        public JavaLanguageWorkerConfig()
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME") ?? string.Empty;
            var javaPath = Path.Combine(javaHome, @"bin\java");
            ExecutablePath = Path.GetFullPath(javaPath);
            var workerJar = Environment.GetEnvironmentVariable("AzureWebJobsJavaWorkerPath");
            if (string.IsNullOrEmpty(workerJar))
            {
                workerJar = Path.Combine(Location, @"workers\java\azure-functions-java-worker.jar");
            }

            WorkerPath = $"-jar {workerJar}";
            Extension = ".jar";
        }
    }
}
