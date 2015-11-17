// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script;

namespace Host.Script

{
    /// <summary>
    /// Sample ScriptHost. 
    /// 
    /// To test the 'processWorkItem' function, you can use message format:
    /// { "ID": "4E3F3E9E-F9CB-41BC-8C6E-808FFCEA2A7B", "Category": "Cleaning", "Description": "Vacuum the floor" }
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            ScriptHost host = ScriptHost.Create();
            host.RunAndBlock();
        }
    }
}
