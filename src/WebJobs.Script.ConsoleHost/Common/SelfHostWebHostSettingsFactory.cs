// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;

namespace WebJobs.Script.ConsoleHost.Common
{
    public static class SelfHostWebHostSettingsFactory
    {
        public static WebHostSettings Create(TraceWriter traceWriter = null)
        {
            return new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(Environment.CurrentDirectory),
                LogPath = Path.Combine(Path.GetTempPath(), @"LogFiles\Application\Functions"),
                SecretsPath = Path.Combine(Environment.CurrentDirectory, "data", "functions", "secrets"),
                TraceWriter = traceWriter
            };
        }
    }
}
