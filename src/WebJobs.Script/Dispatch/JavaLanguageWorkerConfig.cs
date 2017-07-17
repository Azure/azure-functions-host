// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class JavaLanguageWorkerConfig : LanguageWorkerConfig
    {
        public JavaLanguageWorkerConfig()
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            var javaPath = Path.Combine(javaHome, @"..\jdk1.8.0_111\bin\java.exe");
            ExecutablePath = Path.GetFullPath(javaPath);
            WorkerPath = $"-jar {Environment.GetEnvironmentVariable("AzureWebJobsJavaWorkerPath")}";

            // TODO set host and port
            Arguments = "--host 127.0.0.1 --port 50051";
            SupportedScriptTypes = new List<ScriptType>()
            {
                ScriptType.JavaArchive
            };
        }
    }
}
