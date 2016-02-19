// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script;

namespace WebJobs.Script.Host

{
    class Program
    {
        static void Main(string[] args)
        {
            string rootPath = Environment.CurrentDirectory;
            if (args.Length > 0)
            {
                rootPath = (string)args[0];
            }

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath
            };

            ScriptHostManager scriptHostManager = new ScriptHostManager(config);
            scriptHostManager.RunAndBlock();
        }    
    }
}
