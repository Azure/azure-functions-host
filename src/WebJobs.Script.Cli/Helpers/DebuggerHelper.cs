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
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
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

        static readonly string LaunchJsonPath = Path.Combine(Environment.CurrentDirectory, ".vscode", "launch.json");

        public static async Task<bool> AttachManagedAsync(HttpClient server)
        {
            var response = await server.PostAsync("admin/host/debug", new StringContent(string.Empty));
            return response.IsSuccessStatusCode;
        }

        private static async Task<int> GetNodeDebuggerPort(HttpClient server)
        {
            var response = await server.GetAsync("admin/host/status");
            if (response.IsSuccessStatusCode)
            {
                var status = await response.Content.ReadAsAsync<HostStatus>();
                return status.WebHostSettings.NodeDebugPort;
            }
            else
            {
                return -1;
            }
        }

        public static async Task<NodeDebuggerStatus> TryAttachNodeAsync(HttpClient server)
        {
            var nodeDebugPort = await GetNodeDebuggerPort(server);
            if (nodeDebugPort == -1)
            {
                return NodeDebuggerStatus.Error;
            }

            var launchJson = $@"
{{
    ""version"": ""0.2.0"",
    ""configurations"": [
        {{
            ""name"": ""Attach to Azure Functions"",
            ""type"": ""node"",
            ""request"": ""attach"",
            ""port"": {nodeDebugPort}
        }}
    ]
}}";

            var existingLaunchJson = await (FileSystemHelpers.FileExists(LaunchJsonPath)
                ? TaskUtilities.SafeGuardAsync(async () => JsonConvert.DeserializeObject<JObject>(await FileSystemHelpers.ReadAllTextFromFileAsync(LaunchJsonPath)))
                : Task.FromResult<JObject>(null));

            FileSystemHelpers.CreateDirectory(Path.GetDirectoryName(LaunchJsonPath));

            if (existingLaunchJson == null)
            {
                await FileSystemHelpers.WriteAllTextToFileAsync(LaunchJsonPath, launchJson);
                return NodeDebuggerStatus.Created;
            }
            var functionsDebugConfig = existingLaunchJson["configurations"]?.FirstOrDefault(e => e["name"].ToString() == "Attach to Azure Functions");

            if (functionsDebugConfig?["port"]?.ToString() != nodeDebugPort.ToString())
            {
                await FileSystemHelpers.WriteAllTextToFileAsync(LaunchJsonPath, launchJson);
                return NodeDebuggerStatus.Created;
            }
            else if (functionsDebugConfig?["port"]?.ToString() == nodeDebugPort.ToString())
            {
                return NodeDebuggerStatus.AlreadyCreated;
            }
            else
            {
                return NodeDebuggerStatus.Error;
            }
        }
    }
}
