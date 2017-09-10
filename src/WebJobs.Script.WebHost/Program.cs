// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.AspNetCore.Hosting;

namespace WebJobs.Script.WebHost.Core
{
    public class Program
    {
        private static CancellationTokenSource _applicationCts = new CancellationTokenSource();

        public static void Main(string[] args)
        {
            BuildWebHost(args)
                .RunAsync(_applicationCts.Token)
                .Wait();
        }

        public static IWebHost BuildWebHost(string[] args) =>
            Microsoft.AspNetCore.WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .Build();

        internal static void InitiateShutdown()
        {
            _applicationCts.Cancel();
        }
    }
}