// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Script.WebHost;
using WebJobs.Script.Cli.Diagnostics;

namespace WebJobs.Script.Cli.Common
{
    internal static class SelfHostWebHostSettingsFactory
    {
        public static WebHostSettings Create(int nodeDebugPort, TraceLevel consoleTraceLevel)
        {
            return new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(Environment.CurrentDirectory),
                LogPath = Path.Combine(Path.GetTempPath(), @"LogFiles\Application\Functions"),
                SecretsPath = Path.Combine(Environment.CurrentDirectory, "secrets", "functions", "secrets"),
                NodeDebugPort = nodeDebugPort,
                TraceWriter = new ConsoleTraceWriter(consoleTraceLevel)
            };
        }
    }
}
