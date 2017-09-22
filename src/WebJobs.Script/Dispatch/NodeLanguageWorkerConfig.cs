// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class NodeLanguageWorkerConfig : WorkerConfig
    {
        public NodeLanguageWorkerConfig()
            : base()
        {
            ExecutablePath = "node";
            Language = "Node";

            var nodeSection = ScriptSettingsManager.Instance.Configuration
                .GetSection("workers")
                .GetSection(Language);

            var debugPortConfig = nodeSection.GetSection("inspect").Value
                ?? nodeSection.GetSection("debug").Value;

            if (debugPortConfig != null)
            {
                int port = 5858;
                try
                {
                    port = Convert.ToInt32(debugPortConfig);
                }
                catch
                {
                }
                ExecutableArguments.Add($"--inspect={port}");
            }

            WorkerPath = nodeSection.GetSection("path").Value;
            if (string.IsNullOrEmpty(WorkerPath))
            {
                WorkerPath = Path.Combine(Location, "workers", "node", "dist", "src", "nodejsWorker.js");
            }
            Extension = ".js";
        }
    }
}
