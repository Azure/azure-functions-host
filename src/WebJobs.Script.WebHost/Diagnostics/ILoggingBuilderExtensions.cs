// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Logging
{
    public static class ILoggingBuilderExtensions
    {
        public static void AddWebJobsSystem<T>(this ILoggingBuilder builder) where T : SystemLoggerProvider
        {
            builder.Services.AddSingleton<ILoggerProvider, T>();
            builder.Services.Add(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(ScriptLogger<>)));

            // Log all logs to SystemLogger
            builder.AddFilter<T>(null, LogLevel.Trace);
        }
    }
}
