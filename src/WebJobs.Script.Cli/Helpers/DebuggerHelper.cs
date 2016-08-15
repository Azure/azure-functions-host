// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Extensions;

namespace WebJobs.Script.Cli.Helpers
{
    internal enum NodeDebuggerStatus
    {
        Created,
        AlreadyCreated,
        Error
    }

    internal static class DebuggerHelper
    {
        const int Retries = 20;

        static readonly string LaunchJsonPath = Path.Combine(Environment.CurrentDirectory, "launch.json");

        public static async Task<bool> AttachManagedAsync(HttpClient server)
        {
            var response = await server.PostAsync("admin/host/debug", new StringContent(string.Empty));
            return response.IsSuccessStatusCode;
        }

        public static async Task<NodeDebuggerStatus> TryAttachNodeAsync(int processId)
        {
            var tryCount = 0;
            while (tryCount < Retries)
            {
                var hostProcess = Process.GetProcessById(processId);
                if (hostProcess != null)
                {
                    var nodeProcess = hostProcess.GetChildren().FirstOrDefault(p => p.ProcessName == "node");
                    if (nodeProcess == null)
                    {
                        await Task.Delay(200);
                    }
                    else
                    {
                        var launchJson = $@"
{{
    ""version"": ""0.2.0"",
    ""configurations"": [
        {{
            ""name"": ""Attach to Process"",
            ""type"": ""node"",
            ""request"": ""attach"",
            ""processId"": ""{nodeProcess.Id}"",
            ""port"": 5858,
            ""sourceMaps"": false,
            ""outDir"": null
        }}
    ]
}}";
                        var existingLaunchJson = await(FileSystemHelpers.FileExists(LaunchJsonPath)
                            ? Utilities.SafeGuardAsync(async () => JsonConvert.DeserializeObject<JObject>(await FileSystemHelpers.ReadAllTextFromFileAsync(LaunchJsonPath)))
                            : Task.FromResult<JObject>(null));

                        if (existingLaunchJson == null ||
                            existingLaunchJson["configurations"]?["processId"]?.ToString() != nodeProcess.Id.ToString())
                        {
                            await FileSystemHelpers.WriteAllTextToFileAsync(LaunchJsonPath, launchJson);
                            return NodeDebuggerStatus.Created;
                        }
                        else if (existingLaunchJson["configurations"]?["processId"]?.ToString() == nodeProcess.Id.ToString())
                        {
                            return NodeDebuggerStatus.AlreadyCreated;
                        }
                    }
                }
            }
            return NodeDebuggerStatus.Error;
        }
    }
}
