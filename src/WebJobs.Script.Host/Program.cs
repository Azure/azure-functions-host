// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    public static class Program
    {
        public static async Task Main(string[] args)
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

            var host = new HostBuilder()
                .SetAzureFunctionsEnvironment()
                .ConfigureLogging(b =>
                {
                    b.SetMinimumLevel(LogLevel.Information);
                    b.AddConsole();
                })
                .AddScriptHost(o =>
                {
                    o.ScriptPath = rootPath;
                    o.LogPath = Path.Combine(Path.GetTempPath(), "functionshost");
                    o.IsSelfHost = true;
                })
                .UseConsoleLifetime()
                .Build();

            Console.WriteLine("Starting host");

            using (host)
            {
                await host.RunAsync();
            }
        }
    }
}
