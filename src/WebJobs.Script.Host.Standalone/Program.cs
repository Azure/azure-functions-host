// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Logging;

namespace WebJobs.Script.Host.Standalone
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (args == null)
            {
                throw new ArgumentNullException("args");
            }

            string rootPath = Environment.CurrentDirectory;
            if (args.Length > 0)
            {
                rootPath = args[0];
            }

            string rootLogPath = Path.Combine(Path.GetTempPath(), "Functions");
            if (args.Length > 1)
            {
                rootLogPath = args[1];
            }

            var config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath,
                RootLogPath = rootLogPath,
                IsSelfHost = true
            };

            // Add the services for standalone workloads.
            config.HostConfig = config.HostConfig ?? new JobHostConfiguration();
            config.HostConfig.TimerMode = TimerMode.File;
            config.HostConfig.AddService<IDistributedLockManager>(new SqlLeaseDistributedLockManager());
            config.HostConfig.AddService<ILoggerFactory>(new LoggerFactory());
            config.HostConfig.LoggerFactory.AddProvider(new SqlLoggerProvider());

            var scriptHostManager = new ScriptHostManager(config);
            scriptHostManager.RunAndBlock();
        }
    }
}
