// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script;

namespace WebJobs.Script.Host

{
    class Program
    {
        static void Main(string[] args)
        {
            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootPath = args[0]
            };

            ScriptHost host = ScriptHost.Create(config);
            host.RunAndBlock();
        }
    }
}
