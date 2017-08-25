// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class NodeLanguageWorkerConfig : WorkerConfig
    {
        public NodeLanguageWorkerConfig()
        {
            ExecutablePath = "node";
            string value = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebJobsEnvironment);
            if (string.Compare("Development", value, StringComparison.OrdinalIgnoreCase) == 0) {
                ExecutableArguments = new Dictionary<string, string>()
                {
                    ["--inspect"] = string.Empty
                };
            }
            WorkerPath = Environment.GetEnvironmentVariable("NodeJSWorkerPath");
            Extension = ".js";
        }
    }
}
