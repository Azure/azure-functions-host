// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Config;

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

            var config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath,
                IsSelfHost = true
            };

            var scriptHostManager = new ScriptHostManager(config);
            scriptHostManager.RunAndBlock();
        }
    }
}
