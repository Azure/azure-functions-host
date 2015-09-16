// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Framework.Runtime;

namespace Host
{
    /// <summary>
    /// Sample CSharp script host. 
    /// </summary>
    public class Program
    {
        private readonly ILibraryManager _libraryManager;

        public Program(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        public void Main(string[] args)
        {
            JobHostConfiguration config = new JobHostConfiguration();
            config.Tracing.ConsoleLevel = TraceLevel.Verbose;
            config.Queues.MaxPollingInterval = TimeSpan.FromSeconds(2);

            Assembly hostAssembly = Assembly.GetExecutingAssembly();
            ILibraryInformation libInfo = _libraryManager.GetLibraryInformation(hostAssembly.GetName().Name);
            ScriptConfiguration scriptConfig = new ScriptConfiguration()
            {
                ApplicationRootPath = Path.GetDirectoryName(libInfo.Path),
                HostAssembly = hostAssembly
            };
            config.UseScripts(scriptConfig);

            JobHost host = new JobHost(config);
            host.RunAndBlock();
        }
    }
}
