// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebHostSettings
    {
        public bool IsSelfHost { get; set; }

        public string ScriptPath { get; set; }

        public string LogPath { get; set; }

        public string SecretsPath { get; set; }

        // Used by Cli to disable auth when running locally.
        public bool IsAuthDisabled { get; set; } = false;

        public TraceWriter TraceWriter { get; set; }
    }
}