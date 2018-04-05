// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class NodeWorkerProvider : IWorkerProvider
    {
        private string pathToWorkerDir = WorkerProviderHelper.BuildWorkerDirectoryPath(ScriptConstants.NodeLanguageName);

        public WorkerDescription GetDescription() => new WorkerDescription
        {
            Language = ScriptConstants.NodeLanguageName,
            Extension = ".js",
            DefaultExecutablePath = "node",
            DefaultWorkerPath = Path.Combine("dist", "src", "nodejsWorker.js"),
        };

        public bool TryConfigureArguments(ArgumentsDescription args, IConfiguration config, ILogger logger)
        {
            var options = new DefaultWorkerOptions();
            config.GetSection("workers:node").Bind(options);

            if (!string.IsNullOrWhiteSpace(options.Debug))
            {
                args.ExecutableArguments.Add($"--inspect={options.Debug}");
            }
            return true;
        }

        public string GetWorkerDirectoryPath()
        {
            return pathToWorkerDir;
        }
    }
}
