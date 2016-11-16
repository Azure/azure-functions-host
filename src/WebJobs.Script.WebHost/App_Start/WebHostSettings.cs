// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebHostSettings
    {
        [JsonProperty("isSelfHost")]
        public bool IsSelfHost { get; set; }

        [JsonProperty("scriptPath")]
        public string ScriptPath { get; set; }

        [JsonProperty("logPath")]
        public string LogPath { get; set; }

        [JsonProperty("secretsPath")]
        public string SecretsPath { get; set; }

        [JsonProperty("nodeDebugPort")]
        public int NodeDebugPort { get; set; }

        [JsonIgnore]
        public TraceWriter TraceWriter { get; set; }
    }
}