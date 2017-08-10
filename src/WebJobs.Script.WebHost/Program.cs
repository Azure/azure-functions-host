// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting;

namespace WebJobs.Script.WebHost.Core
{
    public class Program
    {
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += Blah;
            BuildWebHost(args).Run();
        }

        private static System.Reflection.Assembly Blah(object sender, ResolveEventArgs args)
        {
            if (args.Name.StartsWith("System.Diagnostics.DiagnosticSource", StringComparison.OrdinalIgnoreCase))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return Assembly.LoadFrom(@"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\2.0.0-preview2-25407-01\System.Diagnostics.DiagnosticSource.dll");
                }
                else
                {
                    return Assembly.LoadFrom(@"/usr/local/share/dotnet/shared/Microsoft.NETCore.App/2.0.0-preview2-25407-01/System.Diagnostics.DiagnosticSource.dll");
                }
            }
            return null;
        }

        public static IWebHost BuildWebHost(string[] args) =>
            Microsoft.AspNetCore.WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .Build();
    }
}