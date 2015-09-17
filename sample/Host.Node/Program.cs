// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;

namespace Host.Node
{
    /// <summary>
    /// Sample Node.js script host. 
    /// 
    /// To test the 'processWorkItem' function, you can use message format:
    /// { "ID": "4E3F3E9E-F9CB-41BC-8C6E-808FFCEA2A7B", "Category": "Cleaning", "Description": "Vacuum the floor" }
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            JobHostConfiguration config = new JobHostConfiguration();
            config.Tracing.ConsoleLevel = TraceLevel.Verbose;
            config.Queues.MaxPollingInterval = TimeSpan.FromSeconds(2);

            ScriptConfiguration scriptConfig = new ScriptConfiguration()
            {
                ApplicationRootPath = Environment.CurrentDirectory,
                HostAssembly = Assembly.GetExecutingAssembly()
            };
            config.UseNodeScripts(scriptConfig);

            JobHost host = new JobHost(config);
            host.RunAndBlock();
        }
    }
}
