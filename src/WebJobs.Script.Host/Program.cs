// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Host
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
                rootPath = (string)args[0];
            }

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath,
                RoleDetectionEnabled = true
            };

            ScriptHostManager scriptHostManager = new ScriptHostManager(config);
            scriptHostManager.RunAndBlock();
        }    
    }
}
