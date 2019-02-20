// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;

namespace Microsoft.Extensions.Hosting
{
    public static class TestHostExtensions
    {
        public static ScriptHost GetScriptHost(this IHost host)
        {
            return host.Services.GetService<IScriptJobHost>() as ScriptHost;
        }

        public static string GetHostId(this IHost host)
        {
            return host.Services.GetService<IHostIdProvider>().GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public static string GetStorageConnectionString(this IHost host)
        {
            return host.Services.GetService<IConfiguration>().GetWebJobsConnectionString("Storage");
        }

        public static TestLoggerProvider GetTestLoggerProvider(this IHost host)
        {
            return host.Services.GetServices<ILoggerProvider>().SingleOrDefault(p => p is TestLoggerProvider) as TestLoggerProvider;
        }
    }
}
