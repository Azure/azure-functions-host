// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Node;

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
            ScriptConfiguration config = new ScriptConfiguration()
            {
                ApplicationRootPath = Environment.CurrentDirectory,
                HostAssembly = Assembly.GetExecutingAssembly()
            };

            NodeScriptHost host = NodeScriptHost.Create(config);
            host.RunAndBlock();
        }
    }
}
