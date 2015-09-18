// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;

namespace Host
{
    /// <summary>
    /// Sample CSharp script host. 
    /// </summary>
    public static class Program
    {
        public static void Main(string[] args)
        {
            JobHostConfiguration config = new JobHostConfiguration();
            config.Tracing.ConsoleLevel = TraceLevel.Verbose;
            config.Queues.MaxPollingInterval = TimeSpan.FromSeconds(2);

            ScriptConfiguration scriptConfig = new ScriptConfiguration()
            {
                ApplicationRootPath = Directory.GetCurrentDirectory(),
                HostAssembly = Assembly.GetExecutingAssembly()
            };
            config.UseScripts(scriptConfig);

            JobHost host = new JobHost(config);
            host.RunAndBlock();
        }
    }
}
