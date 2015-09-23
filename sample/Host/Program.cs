// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Reflection;
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
            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                ApplicationRootPath = Directory.GetCurrentDirectory(),
                HostAssembly = Assembly.GetExecutingAssembly()
            };

            ScriptHost host = CSharpScriptHost.Create(config);
            host.RunAndBlock();
        }
    }
}
